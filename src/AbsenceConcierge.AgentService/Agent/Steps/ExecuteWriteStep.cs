using AbsenceConcierge.AgentService.Telemetry;
using AbsenceConcierge.AgentService.Workforce;

namespace AbsenceConcierge.AgentService.Agent.Steps;

/// <summary>
/// Submits the approved request. Once.
///
/// <para>
/// This step is reachable only from <see cref="ConfirmationDecisionStep"/>, which is
/// reachable only from a typed approval on a draft this conversation was holding.
/// That is C-1 as a property of the pipeline's shape rather than as an instruction
/// in a prompt.
/// </para>
/// <para>
/// <b>It never retries.</b> The attempt policy gives writes one attempt
/// (<see cref="ToolAttemptPolicy"/>), and the reason is worth stating where the call
/// is made: a second attempt against one approval books a second holiday. A write
/// that returned <c>5xx</c> definitely did not land and is reported as not
/// submitted; a write that <em>timed out</em> may or may not have landed and is
/// reported as unknown. Collapsing those two produces either a false failure on a
/// request that succeeded or a double booking, and SPEC §7.2 exists to keep them
/// apart.
/// </para>
/// <para>
/// The correct fix for the indeterminate case is idempotency — a client-supplied key
/// that makes a replay safe — and the estate has no rule for it. Version 1.0.0
/// reports the uncertainty honestly rather than resolving it, which is the weaker
/// answer, and it is recorded as such in <c>docs/DEVIATIONS.md</c> (E-7).
/// </para>
/// </summary>
public sealed class ExecuteWriteStep(IWorkforceTools tools) : IAgentStep
{
    public string Name => "submit_request";

    public bool AppliesTo(AgentTurnContext context) => context?.ApprovedDraft is not null;

    public async ValueTask<StepSignal> ExecuteAsync(
        AgentTurnContext context,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(context);

        var draft = context.ApprovedDraft!;

        // C-5, checked inside the agent as well as at the boundary and in the trace.
        // An identifier that never came from a tool result in this conversation is a
        // hallucination, and the cheapest place to catch it is before the write.
        if (!context.Conversation.IsGrounded(draft.LeaveType.Id))
        {
            context.NoteDegradation(
                AgentDiagnostics.DegradationPhases.Submission,
                WorkforceToolCatalog.RequestTimeOff,
                AgentDiagnostics.DegradationKinds.Malformed);

            return StepSignal.Stop;
        }

        var result = await tools
            .RequestTimeOffAsync(
                new TimeOffRequest(draft.LeaveType.Id, draft.StartDate, draft.EndDate, context.ApprovedToken!),
                cancellationToken)
            .ConfigureAwait(false);

        context.WriteResult = result;

        switch (result.Outcome)
        {
            case ToolOutcome.Success:
                // The reply is composed from this result, not from the draft. B-10:
                // report what the tool returned, never a restatement of what was asked.
                return StepSignal.Continue;

            case ToolOutcome.PermissionDenied:
                context.Refuse(AgentDiagnostics.RefusalRules.MissingCapability);
                return StepSignal.Stop;

            case ToolOutcome.Indeterminate:
                // §7.2: no retry, not once, and the reply says the status is unknown.
                context.NoteDegradation(
                    AgentDiagnostics.DegradationPhases.Submission,
                    WorkforceToolCatalog.RequestTimeOff,
                    AgentDiagnostics.DegradationKinds.Timeout);
                return StepSignal.Stop;

            default:
                // Failed, Rejected, or a confirmation the boundary would not accept.
                // §7 rule 4: never a silent success. The absence of a success claim
                // is itself an assertion.
                context.NoteDegradation(
                    AgentDiagnostics.DegradationPhases.Submission,
                    WorkforceToolCatalog.RequestTimeOff,
                    AgentDiagnostics.DegradationKinds.Error);
                return StepSignal.Stop;
        }
    }
}
