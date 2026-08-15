using System.Globalization;
using System.Net;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace AbsenceConcierge.AgentService.Agent.Llm;

/// <summary>
/// Azure OpenAI, over its chat completions endpoint.
///
/// <para>
/// Raw HTTP rather than a vendor SDK, for the reason P11 gives: no vendor type
/// appears in any signature above <see cref="ILlmProvider"/>, and the surface used
/// here — one POST, one JSON body — is small enough that a client library would add
/// a dependency and a migration rather than remove work.
/// </para>
/// <para>
/// <b>Addressed by deployment name, not model id.</b> That is the single most common
/// way an Azure OpenAI integration fails on its first call, and it is why
/// <see cref="LlmOptions.Model"/> documents which one it holds. The response's own
/// <c>model</c> field is what gets reported back as
/// <see cref="LlmResponse.Model"/> — the model that actually answered, not the one
/// configuration hoped for. ADR-0004 turns on that distinction.
/// </para>
/// </summary>
public sealed class AzureOpenAiLlmProvider(HttpClient client, LlmOptions options, ILogger<AzureOpenAiLlmProvider> logger)
    : ILlmProvider
{
    private static readonly JsonSerializerOptions Json = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    public string Name => LlmProviders.AzureOpenAi;

    public string ConfiguredModel => options.Model ?? "(unset)";

    public async ValueTask<LlmResponse> CompleteAsync(
        LlmRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        var endpoint = (options.Endpoint ?? throw new InvalidOperationException("Llm:Endpoint is not set."))
            .TrimEnd('/');

        var url = string.Create(
            CultureInfo.InvariantCulture,
            $"{endpoint}/openai/deployments/{options.Model}/chat/completions?api-version={options.ApiVersion}");

        var messages = new List<ChatMessage> { new("system", request.SystemPrompt) };
        messages.AddRange(request.Messages.Select(message => new ChatMessage(message.Role, message.Content)));

        var body = new ChatRequest(
            messages,
            request.MaxOutputTokens,
            options.Temperature,
            options.RequireJsonObject ? new ResponseFormat("json_object") : null);

        var response = await SendAsync(url, body, cancellationToken).ConfigureAwait(false);

        var completion = await response.Content
            .ReadFromJsonAsync<ChatResponse>(Json, cancellationToken)
            .ConfigureAwait(false)
            ?? throw new InvalidOperationException("Azure OpenAI returned an empty body.");

        var choice = completion.Choices is { Count: > 0 } choices
            ? choices[0]
            : throw new InvalidOperationException("Azure OpenAI returned no choices.");

        return new LlmResponse(
            choice.Message?.Content ?? string.Empty,

            // Reported by the service, never assumed from configuration. A deployment
            // repointed at a different model is a silent change of measuring stick
            // unless this field is the one that is recorded.
            completion.Model ?? ConfiguredModel,
            completion.Usage?.PromptTokens ?? 0,
            completion.Usage?.CompletionTokens ?? 0,
            choice.FinishReason ?? "unknown");
    }

    /// <summary>
    /// Sends the request, retrying only what is worth retrying.
    ///
    /// <para>
    /// A 429 or a 5xx is weather; a 400 or a 401 is a decision, and retrying it burns
    /// budget to receive the same answer. The bound is two attempts because every
    /// attempt is metered — SPEC §8.1 budgets Layer 2 in money as well as minutes,
    /// and a retry policy is a spending policy whether or not anybody calls it one.
    /// </para>
    /// </summary>
    private async Task<HttpResponseMessage> SendAsync(
        string url,
        ChatRequest body,
        CancellationToken cancellationToken)
    {
        const int MaxAttempts = 2;

        HttpResponseMessage? response = null;

        for (var attempt = 1; attempt <= MaxAttempts; attempt++)
        {
            response?.Dispose();

            using var message = new HttpRequestMessage(HttpMethod.Post, url)
            {
                Content = JsonContent.Create(body, options: Json),
            };

            message.Headers.Add("api-key", options.ApiKey);

            response = await client.SendAsync(message, cancellationToken).ConfigureAwait(false);

            if (response.IsSuccessStatusCode)
            {
                return response;
            }

            var retryable = response.StatusCode is HttpStatusCode.TooManyRequests
                or HttpStatusCode.InternalServerError
                or HttpStatusCode.BadGateway
                or HttpStatusCode.ServiceUnavailable
                or HttpStatusCode.GatewayTimeout;

            if (!retryable || attempt == MaxAttempts)
            {
                break;
            }

            if (logger.IsEnabled(LogLevel.Warning))
            {
                logger.LogWarning(
                    "Azure OpenAI returned {Status}; retrying once (attempt {Attempt} of {MaxAttempts}).",
                    (int)response.StatusCode,
                    attempt,
                    MaxAttempts);
            }
        }

        var status = response!.StatusCode;
        var detail = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
        response.Dispose();

        // The body is included because Azure's 400s are specific and actionable —
        // "max_tokens is not supported with this model" is the one a reader will meet
        // first on an o-series or GPT-5 deployment, which want max_completion_tokens
        // instead. That is a one-line change here, and it is named so it arrives as a
        // recognised message rather than a mystery.
        throw new HttpRequestException(
            $"Azure OpenAI returned {(int)status} {status}. Body: {Truncate(detail)}");
    }

    private static string Truncate(string value) =>
        value.Length <= 500 ? value : value[..500] + "…";

    private sealed record ChatMessage(string Role, string Content);

    private sealed record ResponseFormat(string Type);

    private sealed record ChatRequest(
        IReadOnlyList<ChatMessage> Messages,
        int MaxTokens,
        double? Temperature,
        ResponseFormat? ResponseFormat);

    private sealed record ChatResponse(string? Model, IReadOnlyList<Choice>? Choices, Usage? Usage);

    private sealed record Choice(ChatMessage? Message, string? FinishReason);

    private sealed record Usage(int PromptTokens, int CompletionTokens);
}
