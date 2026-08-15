namespace AbsenceConcierge.AgentService.Agent.Llm;

public static class LlmProviders
{
    /// <summary>
    /// No model. The default, and the mode the whole demonstrated path runs in:
    /// deterministic interpreter, deterministic composer, zero credentials, no
    /// network (ADR-0002).
    /// </summary>
    public const string None = "None";

    /// <summary>Azure OpenAI. Addressed by deployment name, not by model id.</summary>
    public const string AzureOpenAi = "AzureOpenAI";

    /// <summary>Microsoft Foundry, which is where Claude models are served. A different endpoint and client.</summary>
    public const string AnthropicFoundry = "AnthropicFoundry";
}

/// <summary>
/// How a language model is reached, when one is reached at all.
///
/// <para>
/// <b>Nothing here holds a secret in the repository.</b> <see cref="ApiKey"/> is
/// bound from configuration, which locally means <c>dotnet user-secrets</c> and in
/// CI means a GitHub environment secret. An absent key means the provider is not
/// registered and the capability is unavailable — it does not mean a degraded model
/// silently answers instead (P8).
/// </para>
/// </summary>
public sealed class LlmOptions
{
    public const string SectionName = "Llm";

    /// <summary>One of <see cref="LlmProviders"/>. Defaults to no model at all.</summary>
    public string Provider { get; set; } = LlmProviders.None;

    /// <summary>
    /// The Azure OpenAI <b>deployment name</b>, or the Foundry model id, depending on
    /// the provider. These are different kinds of string and conflating them is the
    /// most common way an Azure integration fails at the first call.
    /// </summary>
    public string? Model { get; set; }

    public string? Endpoint { get; set; }

    /// <summary>Never committed. Bound from user secrets locally and an environment secret in CI.</summary>
    public string? ApiKey { get; set; }

    public int MaxOutputTokens { get; set; } = 1024;

    /// <summary>
    /// The judge's model, pinned separately from the agent's.
    ///
    /// AI-EVALS.md §5 requires the judge to be pinned by model and prompt, and a
    /// judge that moves with the agent cannot tell you whether the agent changed:
    /// both sides of the comparison would have moved at once.
    /// </summary>
    public string? JudgeModel { get; set; }

    public bool IsConfigured =>
        !string.Equals(Provider, LlmProviders.None, StringComparison.OrdinalIgnoreCase)
        && !string.IsNullOrWhiteSpace(Model)
        && !string.IsNullOrWhiteSpace(ApiKey);
}
