using AbsenceConcierge.AgentService.Agent.Language;
using AbsenceConcierge.AgentService.Telemetry;
using AbsenceConcierge.AgentService.Workforce;

namespace AbsenceConcierge.AgentService.Agent.Steps;

/// <summary>
/// Establishes who the agent is acting as, before anything else happens.
///
/// <para>
/// It runs on every turn, including the turn that only carries a decision, because
/// the actor's permissions are the authority every later step defers to (SPEC §9)
/// and a stale identity is not a thing this agent is willing to hold.
/// </para>
/// <para>
/// It is also the first place a tool result is scanned for instruction-shaped
/// content. <c>adv-006</c> hides a claim of elevated permissions inside the actor's
/// own display name; the claim is reported here as <c>injection.ignored</c> and has
/// no effect, because the permission check downstream reads the permission list and
/// never the name.
/// </para>
/// </summary>
public sealed class EstablishActorStep(IWorkforceTools tools) : IAgentStep
{
    public string Name => "establish_actor";

    public bool AppliesTo(AgentTurnContext context) => true;

    public async ValueTask<StepSignal> ExecuteAsync(
        AgentTurnContext context,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(context);

        var result = await tools.GetCurrentUserAsync(cancellationToken).ConfigureAwait(false);

        if (!result.IsSuccess || result.Value is null)
        {
            // Without an actor there is nothing this agent can say that is not a
            // guess about whose leave is being discussed.
            context.NoteDegradation(
                AgentDiagnostics.DegradationPhases.EmployeeLookup,
                WorkforceToolCatalog.GetCurrentUser,
                AgentDiagnostics.DegradationKinds.Error);

            return StepSignal.Stop;
        }

        context.Actor = result.Value;

        foreach (var signal in InstructionShapedContent.Scan(result.Value.DisplayName))
        {
            context.NoteIgnoredInstruction(new InstructionShapedFinding(
                "tool_result",
                WorkforceToolCatalog.GetCurrentUser,
                "display_name",
                signal));
        }

        return StepSignal.Continue;
    }
}
