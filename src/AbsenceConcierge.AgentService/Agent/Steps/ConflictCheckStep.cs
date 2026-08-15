using AbsenceConcierge.AgentService.Agent.Language;
using AbsenceConcierge.AgentService.Telemetry;
using AbsenceConcierge.AgentService.Workforce;

namespace AbsenceConcierge.AgentService.Agent.Steps;

/// <summary>
/// Checks the actor's existing bookings for an overlap, before anything is drafted
/// (B-4).
///
/// <para>
/// Two behaviours worth reading side by side, because they look contradictory and
/// are not:
/// </para>
/// <list type="bullet">
///   <item><b>An overlap stops the turn.</b> The agent reports the collision and
///     asks; it does not draft over an existing booking and invite a human to
///     rubber-stamp the clash (B-5).</item>
///   <item><b>A failed check does not.</b> If the tool errors, the agent still
///     drafts — clearly marked as unverified, with <c>degradation.noted</c> beside
///     the confirmation (§7 rule 5). Abandoning an answerable request because one
///     read failed is a denial of service the agent inflicted on itself; drafting as
///     though the check had passed is presenting an unverified request as a verified
///     one. Both are defects, and the correct behaviour sits between them.</item>
/// </list>
/// </summary>
public sealed class ConflictCheckStep(IWorkforceTools tools) : IAgentStep
{
    public string Name => "check_conflicts";

    public bool AppliesTo(AgentTurnContext context) =>
        context?.Intent is { Kind: IntentKind.RequestTimeOff } && context.Dates?.IsResolved == true;

    public async ValueTask<StepSignal> ExecuteAsync(
        AgentTurnContext context,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(context);

        var result = await tools.ListLeavesAsync(cancellationToken).ConfigureAwait(false);

        if (!result.IsSuccess || result.Value is null)
        {
            context.ConflictCheck = AgentDiagnostics.ConflictCheckStates.NotRun;
            context.NoteDegradation(
                AgentDiagnostics.DegradationPhases.ConflictCheck,
                WorkforceToolCatalog.ListLeaves,
                result.Outcome == ToolOutcome.Indeterminate
                    ? AgentDiagnostics.DegradationKinds.Timeout
                    : AgentDiagnostics.DegradationKinds.Error);

            return StepSignal.Continue;
        }

        foreach (var leave in result.Value)
        {
            foreach (var signal in InstructionShapedContent.Scan(leave.Comment))
            {
                context.NoteIgnoredInstruction(new InstructionShapedFinding(
                    "tool_result",
                    WorkforceToolCatalog.ListLeaves,
                    "comment",
                    signal));
            }
        }

        var start = context.Dates!.Start!.Value;
        var end = context.Dates.End!.Value;

        var overlaps = result.Value
            .Where(leave => leave.StartDate <= end && start <= leave.EndDate)
            .ToList();

        if (overlaps.Count > 0)
        {
            context.ConflictingLeaves = overlaps;
            context.ConflictCheck = AgentDiagnostics.ConflictCheckStates.ConflictsFound;
            context.AskFor(AgentDiagnostics.ClarificationReasons.ConflictingBooking);
            return StepSignal.Stop;
        }

        context.ConflictCheck = AgentDiagnostics.ConflictCheckStates.Clean;
        return StepSignal.Continue;
    }
}
