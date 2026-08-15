using System.Globalization;
using AbsenceConcierge.AgentService.Telemetry;
using AbsenceConcierge.AgentService.Workforce.Confirmation;

namespace AbsenceConcierge.AgentService.Agent.Steps;

/// <summary>
/// The gate. The turn stops here, and a human decides.
///
/// <para>
/// This is the whole premise of the agent, so it is worth being precise about what
/// stops the write. Not this step: this step ends the turn, mints a token bound to
/// this exact draft, and emits <c>confirmation.shown</c>. What stops the write is
/// that <c>request_time_off</c> will not accept a token that was never approved, and
/// the token store enforces that at the boundary regardless of what the agent
/// decides. An agent that can only be stopped by its own prompt is not
/// human-in-the-loop, whatever the prompt says (AI-EVALS.md §8).
/// </para>
/// <para>
/// The token is minted here and released only by <see cref="ConfirmationDecisionStep"/>.
/// It never reaches the trace: it authorises a write, which makes it a credential,
/// and traces get exported to places this service does not control.
/// </para>
/// </summary>
public sealed class ConfirmationGateStep(IConfirmationTokenStore tokens) : IAgentStep
{
    public string Name => "confirmation_gate";

    public bool AppliesTo(AgentTurnContext context) =>
        context is { Draft: not null, ApprovedDraft: null };

    public ValueTask<StepSignal> ExecuteAsync(AgentTurnContext context, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(context);

        var draft = context.Draft!;

        var token = tokens.Issue(new ConfirmationDraft(
            draft.EmployeeId,
            draft.LeaveType.Id,
            draft.StartDate,
            draft.EndDate));

        context.Conversation.HoldDraft(draft, token);

        context.EmitEvent(
            AgentDiagnostics.Events.ConfirmationShown,
            (AgentDiagnostics.Attributes.ConfirmationEmployeeId, draft.EmployeeId),
            (AgentDiagnostics.Attributes.ConfirmationLeaveTypeId, draft.LeaveType.Id),
            (AgentDiagnostics.Attributes.ConfirmationLeaveTypeName, draft.LeaveType.Name),
            (AgentDiagnostics.Attributes.ConfirmationStartDate, Iso(draft.StartDate)),
            (AgentDiagnostics.Attributes.ConfirmationEndDate, Iso(draft.EndDate)),
            (AgentDiagnostics.Attributes.ConfirmationWorkingDays, draft.WorkingDays),
            (AgentDiagnostics.Attributes.ConfirmationExcludedDays, DescribeExcluded(draft)),
            (AgentDiagnostics.Attributes.ConfirmationAttachmentRequired, draft.AttachmentRequired),
            (AgentDiagnostics.Attributes.ConfirmationConflictCheck, draft.ConflictCheck));

        context.Outcomes.Record(AgentDiagnostics.TurnOutcomes.ConfirmationPending);

        return ValueTask.FromResult(StepSignal.Stop);
    }

    private static string Iso(DateOnly date) => date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);

    /// <summary>
    /// The excluded days as <c>date=reason</c> pairs. A count alone cannot be checked
    /// by the human approving it, which is what B-11 is about — and a structured
    /// string is assertable where a sentence is not (ADR-0003).
    /// </summary>
    private static string DescribeExcluded(LeaveDraft draft) =>
        string.Join(";", draft.ExcludedDays.Select(day => $"{Iso(day.Date)}={day.Reason}"));
}
