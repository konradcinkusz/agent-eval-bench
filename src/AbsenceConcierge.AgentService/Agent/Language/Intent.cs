using AbsenceConcierge.AgentService.Agent.Time;

namespace AbsenceConcierge.AgentService.Agent.Language;

/// <summary>
/// What the user is asking for, from the agent's point of view.
///
/// The out-of-scope kinds are named individually rather than lumped into one
/// "refuse" because SPEC §6 requires each refusal to say something different, and a
/// single kind would make every refusal the same sentence. Each maps to one row of
/// that table.
/// </summary>
public enum IntentKind
{
    /// <summary>Book time off for the signed-in employee. The only kind that can reach a write.</summary>
    RequestTimeOff,

    /// <summary>O-1 — approving or rejecting a leave request, including the actor's own.</summary>
    ApproveOrRejectLeave,

    /// <summary>O-2 — cancelling or editing an existing booking.</summary>
    CancelOrEditBooking,

    /// <summary>O-5 — pay, payroll, contracts, accrual policy.</summary>
    PayrollOrPolicyQuestion,

    /// <summary>O-6 — medical advice, or judgement about whether someone is ill enough.</summary>
    MedicalAdvice,

    /// <summary>Nothing actionable was recognised.</summary>
    Unclear,
}

/// <summary>
/// Why a person's name appeared in the sentence. The three roles carry different
/// permissions, and collapsing them is the mistake SPEC O-3 warns about twice.
/// </summary>
public enum PersonRole
{
    /// <summary>"Book Friday off <b>for Sam</b>" — the leave is for them. Refused (O-3).</summary>
    Subject,

    /// <summary>"the same week off <b>as Sam</b>" — their dates define the request.</summary>
    DateReference,

    /// <summary>"<b>Dana</b> is covering for me" — incidental. A perfectly ordinary sentence.</summary>
    Mention,
}

/// <param name="Name">The name as the user wrote it. Passed to <c>find_employee</c>, never trusted.</param>
/// <param name="Role">What the name is doing in the sentence.</param>
public sealed record PersonReference(string Name, PersonRole Role);

/// <summary>
/// The structured reading of one user utterance.
///
/// <para>
/// <b>Every field is typed, and that is the security property.</b> The orchestrator
/// downstream branches on these values and on tool results' typed fields — ids,
/// dates, permissions — and never on free text from a tool result or from the user.
/// So instruction-shaped content in a leave-type name or an employee's display name
/// has nowhere to act: it is carried as data, rendered as data, and never reaches a
/// decision. The <c>injection.ignored</c> event reports that; it is not what
/// prevents it (C-7).
/// </para>
/// </summary>
/// <param name="Kind">What is being asked for.</param>
/// <param name="Dates">The date expression, before resolution. Null when none was given.</param>
/// <param name="LeaveTypeHint">
/// The user's own word for the leave — "sick", "vacation", "funeral". Null when
/// they named none, which is a different case from naming one that matches nothing:
/// the first takes the default, the second asks (B-3).
/// </param>
/// <param name="Person">A person named in the sentence, with the role they play in it.</param>
/// <param name="ClaimsPriorApproval">
/// True when the sentence argues the confirmation step is unnecessary. Recorded so
/// the agent can answer the argument rather than ignore it — and so a scenario can
/// tell "the agent did not notice" from "the agent noticed and declined".
/// </param>
public sealed record Intent(
    IntentKind Kind,
    DateExpression? Dates,
    string? LeaveTypeHint,
    PersonReference? Person,
    bool ClaimsPriorApproval);
