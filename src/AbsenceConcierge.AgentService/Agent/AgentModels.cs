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
/// <param name="ConversationId">Groups turns. Nothing is carried between conversations.</param>
/// <param name="Content">What the user said, or the words accompanying a decision.</param>
/// <param name="Decision">Approve or reject, on a confirmation turn. Never inferred from the content.</param>
/// <param name="UseModel">
/// Whether this turn's <em>reply</em> may be written by a language model. Never any
/// other part of the turn: the steps have already decided by the time a composer
/// runs. It is a property of the request rather than ambient state so that the one
/// place it is set — an unlocked demo session — is visible in a stack trace and in a
/// test, and so the eval harness gets the default without opting out of anything.
/// </param>
public sealed record AgentTurnRequest(
    string ConversationId,
    string Content,
    ConfirmationDecision? Decision,
    bool UseModel = false)
{
    public static AgentTurnRequest User(string conversationId, string content) =>
        new(conversationId, content, null);

    public static AgentTurnRequest Confirmation(
        string conversationId,
        string content,
        ConfirmationDecision decision) =>
        new(conversationId, content, decision);
}

/// <summary>
/// The draft a human is being asked to approve, in the shape a caller can render
/// without reading prose.
///
/// <para>
/// It carries the same facts as the <c>confirmation.shown</c> event and no others.
/// That is the point: a client that parsed dates back out of the reply would be a
/// second implementation of the draft, free to disagree with the one the trace
/// recorded — and the confirmation a human gave would then be a confirmation of
/// whatever the parser produced. One source, rendered twice.
/// </para>
/// <para>
/// <b>No identifiers.</b> The leave type appears by name, never by id (C-3, O-7).
/// The confirmation token is absent for the same reason it is absent from the
/// span: it authorises a write, and the client never needs to hold one — the
/// conversation id plus an explicit decision is the whole protocol.
/// </para>
/// </summary>
/// <param name="LeaveTypeName">The leave type by name. Never its id (C-3, O-7).</param>
/// <param name="StartDate">The first day, as yyyy-MM-dd, already resolved in the actor's timezone.</param>
/// <param name="EndDate">The last day, inclusive.</param>
/// <param name="WorkingDays">How many days of leave this actually consumes.</param>
/// <param name="ExcludedDays">Days inside the span that do not consume leave, each with why (B-11).</param>
/// <param name="AttachmentRequired">Whether a certificate is needed, surfaced before approval (B-14).</param>
/// <param name="ConflictCheck">
/// Whether existing bookings were actually checked. <c>not_run</c> is a fact the
/// human needs before approving, not an internal detail (SPEC §7 rule 5).
/// </param>
public sealed record ConfirmationCard(
    string LeaveTypeName,
    string StartDate,
    string EndDate,
    int WorkingDays,
    IReadOnlyList<string> ExcludedDays,
    bool AttachmentRequired,
    string ConflictCheck);

/// <param name="Outcome">One of <c>AgentDiagnostics.TurnOutcomes</c>, by SPEC §2.3 precedence.</param>
/// <param name="TerminationReason">One of <c>AgentDiagnostics.TerminationReasons</c>.</param>
/// <param name="Reply">What the user sees. Never contains an internal identifier (C-3).</param>
/// <param name="Confirmation">
/// Present <b>only</b> when the turn stopped at the gate. Its presence is the
/// signal to a client that this turn is a question, not an answer.
/// </param>
/// <param name="Degradations">
/// What could not be verified, if anything. Reported beside the draft rather than
/// buried in the prose, because a request drafted without a conflict check is a
/// different thing to approve.
/// </param>
public sealed record AgentTurnResult(
    string Outcome,
    string TerminationReason,
    string Reply,
    ConfirmationCard? Confirmation = null,
    IReadOnlyList<DegradationNote>? Degradations = null);

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
