namespace AbsenceConcierge.AgentService.Agent.Llm;

public sealed record LlmMessage(string Role, string Content);

/// <param name="SystemPrompt">The agent's instructions. Versioned in <c>prompts/</c>, never inline.</param>
/// <param name="Messages">The conversation so far.</param>
/// <param name="MaxOutputTokens">A ceiling, because an unbounded generation is an unbounded bill.</param>
public sealed record LlmRequest(string SystemPrompt, IReadOnlyList<LlmMessage> Messages, int MaxOutputTokens);

/// <param name="Text">What the model said.</param>
/// <param name="Model">
/// The model that actually answered — reported by the provider, not assumed from
/// configuration. If a fallback ever occurs, this is the field that says so, and it
/// is recorded on the span for the reason set out in ADR-0004.
/// </param>
/// <param name="InputTokens">For the cost budget in SPEC §8.1, which is metered in money as well as minutes.</param>
/// <param name="OutputTokens">As above.</param>
/// <param name="FinishReason">Whether it stopped because it was done or because it hit the ceiling.</param>
public sealed record LlmResponse(
    string Text,
    string Model,
    int InputTokens,
    int OutputTokens,
    string FinishReason);

/// <summary>
/// The one interface a language model is reached through.
///
/// <para>
/// There is deliberately no vendor name below this line, and no vendor SDK type in
/// any signature (P11). Two providers are in scope for this repository and they are
/// not the same surface: Azure OpenAI serves OpenAI models and is addressed by
/// <em>deployment name</em>, while Claude models are served through Microsoft
/// Foundry, which is a different endpoint with a different client. "Azure, with a
/// Claude option" is two adapters, not one configuration flag — which is precisely
/// why they sit behind one interface here.
/// </para>
/// <para>
/// <b>No silent fallback.</b> If a configured model is unavailable, an
/// implementation may fall back only if it reports the model that actually answered
/// in <see cref="LlmResponse.Model"/>, and the caller records it. An eval baseline
/// gathered under one model does not describe another, and a fallback nobody can see
/// in the trace turns the baseline into a measuring stick that changes length — the
/// same defect AI-EVALS.md §5 names for an unpinned judge. See ADR-0004.
/// </para>
/// </summary>
public interface ILlmProvider
{
    /// <summary>Short, stable provider name for the trace.</summary>
    string Name { get; }

    /// <summary>The model this provider is configured to call.</summary>
    string ConfiguredModel { get; }

    ValueTask<LlmResponse> CompleteAsync(LlmRequest request, CancellationToken cancellationToken = default);
}
