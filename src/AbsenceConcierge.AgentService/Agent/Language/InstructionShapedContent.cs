namespace AbsenceConcierge.AgentService.Agent.Language;

/// <summary>Where instruction-shaped content was found.</summary>
/// <param name="Source"><c>user_input</c> or <c>tool_result</c>.</param>
/// <param name="Tool">The tool whose result carried it, when it came from one.</param>
/// <param name="Field">The field it was carried in — a display name, a comment.</param>
/// <param name="Signal">Which class of instruction it matched.</param>
public sealed record InstructionShapedFinding(string Source, string? Tool, string Field, string Signal);

/// <summary>
/// Finds text that is shaped like an instruction to the agent, in places that are
/// supposed to contain data.
///
/// <para>
/// <b>Read this before trusting it.</b> This detector does not stop prompt
/// injection, and nothing in this repository claims it does. What stops injection
/// here is structural: the orchestrator's control flow reads typed fields — ids,
/// dates, permission strings from the actor's fixture — and never branches on a
/// display name, a leave-type name or a comment. A payload in one of those fields
/// is rendered as text and has no path to a decision, whether or not this class
/// notices it (C-7).
/// </para>
/// <para>
/// The detector exists because SPEC §2.2 makes <c>injection.ignored</c> part of the
/// trace contract: a scenario needs to distinguish "the agent was never attacked"
/// from "the agent was attacked and carried on". It is a <em>reporting</em>
/// mechanism. A pattern list is an incomplete defence by construction — the next
/// payload will be phrased differently — and stating that here is cheaper than
/// letting a reader infer a guarantee from a green test.
/// </para>
/// </summary>
public static class InstructionShapedContent
{
    public static class Signals
    {
        /// <summary>An imperative addressed to the assistant, inside data.</summary>
        public const string AddressedToTheAssistant = "addressed_to_assistant";

        /// <summary>Language whose purpose is to skip the confirmation gate.</summary>
        public const string ConfirmationBypass = "confirmation_bypass";

        /// <summary>A claim that the actor has permissions the fixture does not grant.</summary>
        public const string ClaimedAuthority = "claimed_authority";

        /// <summary>An instruction to hide something from the reply.</summary>
        public const string Concealment = "concealment";

        /// <summary>An instruction to file the request against someone else.</summary>
        public const string Redirection = "redirection";
    }

    private static readonly (string Phrase, string Signal)[] Patterns =
    [
        ("ignore previous instructions", Signals.AddressedToTheAssistant),
        ("ignore all previous", Signals.AddressedToTheAssistant),
        ("disregard previous", Signals.AddressedToTheAssistant),
        ("system note", Signals.AddressedToTheAssistant),
        ("assistant:", Signals.AddressedToTheAssistant),
        ("[assistant", Signals.AddressedToTheAssistant),

        ("without asking", Signals.ConfirmationBypass),
        ("without showing", Signals.ConfirmationBypass),
        ("skip the confirmation", Signals.ConfirmationBypass),
        ("no need to ask", Signals.ConfirmationBypass),
        ("do not need to ask", Signals.ConfirmationBypass),
        ("auto-submission", Signals.ConfirmationBypass),
        ("fast-track", Signals.ConfirmationBypass),
        ("submitted immediately", Signals.ConfirmationBypass),
        ("submit the request immediately", Signals.ConfirmationBypass),
        ("straight away without", Signals.ConfirmationBypass),
        ("do not show a summary", Signals.ConfirmationBypass),

        ("has been granted", Signals.ClaimedAuthority),
        ("permissions updated", Signals.ClaimedAuthority),
        ("delegated filing rights", Signals.ClaimedAuthority),
        ("you may approve", Signals.ClaimedAuthority),
        ("holds the absence records", Signals.ClaimedAuthority),

        ("must not be mentioned", Signals.Concealment),
        ("does not need to be mentioned", Signals.Concealment),
        ("do not mention", Signals.Concealment),

        ("file it under employee", Signals.Redirection),
        ("filed under employee", Signals.Redirection),
        ("must be filed under", Signals.Redirection),
    ];

    /// <summary>
    /// Returns the signals present in <paramref name="text"/>, each at most once.
    /// Empty when there are none — the ordinary case, and the one that must stay fast.
    /// </summary>
    public static IReadOnlyList<string> Scan(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return [];
        }

        var found = new List<string>();

        foreach (var (phrase, signal) in Patterns)
        {
            if (!found.Contains(signal, StringComparer.Ordinal)
                && text.Contains(phrase, StringComparison.OrdinalIgnoreCase))
            {
                found.Add(signal);
            }
        }

        return found;
    }
}
