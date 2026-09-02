using AbsenceConcierge.AgentService.Telemetry;

namespace AbsenceConcierge.AgentService.Agent.Steps;

/// <summary>
/// Assembles the request the human is going to be shown.
///
/// <para>
/// Everything computed here ends up on <c>confirmation.shown</c> as an attribute:
/// the leave type's name, the resolved dates, the working days consumed, the days
/// excluded and why, whether a certificate is required, and whether the conflict
/// check actually ran. Those are deterministic facts, and leaving them only in the
/// prose would make B-11 and B-14 gradeable by nothing but the judge (SPEC §2.2).
/// </para>
/// <para>
/// The certificate threshold is counted in <em>working</em> days, matching the
/// number the confirmation states. A rule that quietly counted calendar days would
/// surface the requirement on a request the reader was told costs three days,
/// and a mismatch a user cannot see is a mismatch they cannot query.
/// </para>
/// </summary>
public sealed class DraftStep : IAgentStep
{
    public string Name => "draft_request";

    public bool AppliesTo(AgentTurnContext context) =>
        context is { SelectedLeaveType: not null, Actor: not null } && context.Dates?.IsResolved == true;

    public ValueTask<StepSignal> ExecuteAsync(AgentTurnContext context, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(context);

        var leaveType = context.SelectedLeaveType!;
        var start = context.Dates!.Start!.Value;
        var end = context.Dates.End!.Value;
        var count = context.Calendar.Count(start, end);

        // O-8. Calendar days, not working days: "consecutive" is a claim about an
        // unbroken run, and a run that skips the weekend is not one. The fixture
        // agrees — parental leave's 112 is sixteen weeks exactly, which working
        // days would make twenty-two.
        //
        // Checked before the draft exists rather than surfaced on it, because a
        // confirmation card is a thing the user is asked to approve (C-6) and a
        // request the policy already rejects is not approvable. The field has been
        // in the fixture since the first world and was read by nothing until now.
        if (leaveType.MaxConsecutiveDays > 0
            && end.DayNumber - start.DayNumber + 1 > leaveType.MaxConsecutiveDays)
        {
            context.Refuse(AgentDiagnostics.RefusalRules.ExceedsMaximumLength);
            return ValueTask.FromResult(StepSignal.Stop);
        }

        var attachmentRequired =
            leaveType.RequiresAttachmentAfterDays is { } threshold && count.WorkingDays > threshold;

        context.Draft = new LeaveDraft(
            context.Actor!.EmployeeId,
            leaveType,
            start,
            end,
            count.WorkingDays,
            count.Excluded,
            attachmentRequired,
            context.ConflictCheck);

        return ValueTask.FromResult(StepSignal.Continue);
    }
}
