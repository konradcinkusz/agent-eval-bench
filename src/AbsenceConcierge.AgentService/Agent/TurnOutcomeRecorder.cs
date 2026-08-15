using AbsenceConcierge.AgentService.Telemetry;

namespace AbsenceConcierge.AgentService.Agent;

/// <summary>
/// Collects the outcomes a turn could claim and returns the one that wins.
///
/// <para>
/// SPEC §2.3 fixes the precedence — <c>refused</c> › <c>degraded</c> ›
/// <c>clarification_requested</c> › <c>confirmation_pending</c> › <c>cancelled</c>
/// › <c>completed</c> — and the ordering is not arbitrary: it ranks what the user
/// needs to know first. The failure it guards against is a turn that looks routine
/// while something underneath it did not work, which is precisely <c>deg-002</c>: a
/// draft was shown <em>and</em> a read failed, and reporting that turn as
/// <c>confirmation_pending</c> would hide the half that matters.
/// </para>
/// <para>
/// It lives in one class rather than in the steps because a precedence implemented
/// by whichever step happened to write the attribute last is a convention, not a
/// contract, and cannot be unit-tested on its own.
/// </para>
/// </summary>
public sealed class TurnOutcomeRecorder
{
    private static readonly string[] Precedence =
    [
        AgentDiagnostics.TurnOutcomes.Refused,
        AgentDiagnostics.TurnOutcomes.Degraded,
        AgentDiagnostics.TurnOutcomes.ClarificationRequested,
        AgentDiagnostics.TurnOutcomes.ConfirmationPending,
        AgentDiagnostics.TurnOutcomes.Cancelled,
        AgentDiagnostics.TurnOutcomes.Completed,
    ];

    private readonly HashSet<string> _recorded = new(StringComparer.Ordinal);

    public void Record(string outcome)
    {
        if (Array.IndexOf(Precedence, outcome) < 0)
        {
            // An outcome outside the closed set means someone added a turn state
            // and did not add it to SPEC §2.3. Failing here is the cheapest place
            // to find that out; the alternative is a scenario asserting an outcome
            // the harness has never seen and cannot explain.
            throw new ArgumentOutOfRangeException(
                nameof(outcome),
                outcome,
                "Not a turn outcome. The closed set is defined in docs/SPEC.md §2.3.");
        }

        _recorded.Add(outcome);
    }

    /// <summary>
    /// The single outcome for this turn. Defaults to <c>completed</c> only when a
    /// turn ran to the end without any step claiming anything — which is the happy
    /// path after a write.
    /// </summary>
    public string Resolve() =>
        Precedence.FirstOrDefault(_recorded.Contains) ?? AgentDiagnostics.TurnOutcomes.Completed;
}
