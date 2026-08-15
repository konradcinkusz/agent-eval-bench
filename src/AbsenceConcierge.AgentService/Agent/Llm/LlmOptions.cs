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
    /// Azure OpenAI's API version. Pinned rather than floating: the request body's
    /// accepted fields change between versions, and a suite whose transport changed
    /// under it would report a model regression.
    /// </summary>
    public string ApiVersion { get; set; } = "2024-10-21";

    /// <summary>
    /// Zero by default, because a judge that samples is a judge whose score moves
    /// without the agent moving. Nullable so it can be omitted entirely: some newer
    /// models reject any non-default temperature and fail the call rather than
    /// ignoring it.
    /// </summary>
    public double? Temperature { get; set; }

    /// <summary>
    /// Ask for a JSON object back. The judge's output is parsed strictly, and a
    /// prose preamble around the JSON is the most common reason a scoring run fails
    /// for a reason that has nothing to do with the agent.
    /// </summary>
    public bool RequireJsonObject { get; set; } = true;

    /// <summary>
    /// The judge's model, pinned separately from the agent's.
    ///
    /// AI-EVALS.md §5 requires the judge to be pinned by model and prompt, and a
    /// judge that moves with the agent cannot tell you whether the agent changed:
    /// both sides of the comparison would have moved at once.
    /// </summary>
    public string? JudgeModel { get; set; }

    /// <summary>
    /// Rate for the configured model, in dollars per million input tokens.
    ///
    /// Optional, and deliberately not defaulted to a number. SPEC §8.1 budgets
    /// Layer 2 in money as well as minutes, but rates change, differ per deployment
    /// and per region, and a stale figure committed to a public repository is a
    /// figure somebody will quote back. Tokens are always reported because they are
    /// the fact; dollars appear only when someone supplies the rate.
    /// </summary>
    public decimal? PricePerMillionInputTokens { get; set; }

    /// <summary>As above, for output tokens.</summary>
    public decimal? PricePerMillionOutputTokens { get; set; }

    public bool IsConfigured =>
        !string.Equals(Provider, LlmProviders.None, StringComparison.OrdinalIgnoreCase)
        && !string.IsNullOrWhiteSpace(Model)
        && !string.IsNullOrWhiteSpace(ApiKey)
        && !string.IsNullOrWhiteSpace(Endpoint);

    /// <summary>
    /// A copy configured for the judge rather than the agent.
    ///
    /// The judge is pinned separately (ADR-0004): if both move together, a changed
    /// score cannot be attributed, because both sides of the comparison moved at
    /// once. When no judge model is named this returns null rather than quietly
    /// grading with the agent's own — which is the failure the separate pin exists
    /// to prevent.
    /// </summary>
    public LlmOptions? ForJudge()
    {
        if (string.IsNullOrWhiteSpace(JudgeModel))
        {
            return null;
        }

        return new LlmOptions
        {
            Provider = Provider,
            Model = JudgeModel,
            Endpoint = Endpoint,
            ApiKey = ApiKey,
            ApiVersion = ApiVersion,
            MaxOutputTokens = MaxOutputTokens,
            Temperature = Temperature,
            RequireJsonObject = RequireJsonObject,
            PricePerMillionInputTokens = PricePerMillionInputTokens,
            PricePerMillionOutputTokens = PricePerMillionOutputTokens,
        };
    }
}
