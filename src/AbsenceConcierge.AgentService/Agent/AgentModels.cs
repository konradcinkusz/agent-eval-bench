using AbsenceConcierge.AgentService.Agent.Time;
using AbsenceConcierge.AgentService.Workforce;

namespace AbsenceConcierge.AgentService.Agent;

/// <summary>A human's decision on a specific drafted request.</summary>
public enum ConfirmationDecision
{
    Approve,
    Reject,
}

/// <summary>
/// One turn of input.
///
/// <para>
/// <see cref="Decision"/> is a separate field rather than a sentence the agent
/// interprets, and that is the same design decision the scenario schema makes when
/// it gives <c>confirmation</c> its own role. A confirmation is not chat: modelling
/// it as text would let a plausible-sounding sentence stand in for an explicit
/// approval, which is exactly the substitution <c>adv-002</c> attempts.
/// </para>
/// </summary>
public sealed record AgentTurnRequest(string ConversationId, string Content, ConfirmationDecision? Decision)
{
    public static AgentTurnRequest User(string conversationId, string content) =>
        new(conversationId, content, null);

    public static AgentTurnRequest Confirmation(
        string conversationId,
        string content,
        ConfirmationDecision decision) =>
        new(conversationId, content, decision);
}

/// <param name="Outcome">One of <c>AgentDiagnostics.TurnOutcomes</c>, by SPEC §2.3 precedence.</param>
/// <param name="TerminationReason">One of <c>AgentDiagnostics.TerminationReasons</c>.</param>
/// <param name="Reply">What the user sees. Never contains an internal identifier (C-3).</param>
public sealed record AgentTurnResult(string Outcome, string TerminationReason, string Reply);

/// <summary>
/// A request the agent has assembled and is about to show a human.
///
/// <para>
/// Every field here becomes an attribute on <c>confirmation.shown</c>. That is not
/// convenience: B-11 ("says which days were excluded") and B-14 ("surfaces the
/// certificate requirement") are deterministic facts, and leaving them only in the
/// prose would make them gradeable by nothing but the judge (SPEC §2.2).
/// </para>
/// </summary>
public sealed record LeaveDraft(
    string EmployeeId,
    LeaveType LeaveType,
    DateOnly StartDate,
    DateOnly EndDate,
    int WorkingDays,
    IReadOnlyList<ExcludedDay> ExcludedDays,
    bool AttachmentRequired,
    string ConflictCheck);
