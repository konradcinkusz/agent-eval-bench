using Microsoft.Extensions.Logging.Abstractions;

namespace AbsenceConcierge.AgentService.Agent.Llm;

/// <summary>
/// Builds the provider named in configuration, or explains why it cannot.
///
/// <para>
/// <b>An unconfigured provider returns null; an unimplemented one throws.</b> The
/// difference is the whole of P8. "No key is present" is an expected state — the
/// entire demonstrated path runs in it — and the caller degrades to the working
/// fallback with a line saying what was missing. "You asked for a provider this
/// repository does not have" is a configuration error, and returning null for it
/// would let a run report `skipped:no-credential` when the truth is that the code
/// was never written. Those are different facts and SPEC §8.5 is explicit that they
/// are reported differently.
/// </para>
/// </summary>
public static class LlmProviderFactory
{
    public static ILlmProvider? Create(LlmOptions options, HttpClient client)
    {
        ArgumentNullException.ThrowIfNull(options);

        if (string.Equals(options.Provider, LlmProviders.None, StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        if (string.Equals(options.Provider, LlmProviders.AzureOpenAi, StringComparison.OrdinalIgnoreCase))
        {
            return options.IsConfigured
                ? new AzureOpenAiLlmProvider(client, options, NullLogger<AzureOpenAiLlmProvider>.Instance)
                : null;
        }

        if (string.Equals(options.Provider, LlmProviders.AnthropicFoundry, StringComparison.OrdinalIgnoreCase))
        {
            // Deliberately not a silent null. The interface, the options and the
            // report all support a second provider; what does not exist yet is an
            // adapter, and shipping one nobody could run against a credential would
            // be the "latent capability" that TESTING-STRATEGY.md §9 calls
            // documentation that lies. Recorded as D-8 in docs/DEVIATIONS.md.
            throw new NotSupportedException(
                "Llm:Provider is 'AnthropicFoundry'. Claude models are served through Microsoft Foundry, "
                + "which is a different endpoint and client from Azure OpenAI, and that adapter is not "
                + "implemented yet (docs/DEVIATIONS.md D-8). Use 'AzureOpenAI', or 'None' to run with no "
                + "model at all.");
        }

        throw new NotSupportedException(
            $"Unknown Llm:Provider '{options.Provider}'. Valid values: "
            + $"{LlmProviders.None}, {LlmProviders.AzureOpenAi}, {LlmProviders.AnthropicFoundry}.");
    }
}
