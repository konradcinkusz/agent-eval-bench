using AbsenceConcierge.AgentService.Telemetry;
using AbsenceConcierge.AgentService.Workforce.Confirmation;

namespace AbsenceConcierge.AgentService.Agent.Steps;

/// <summary>
/// Consumes a human's decision on the draft this conversation is holding.
///
/// <para>
/// This step is the only route from a draft to a write, and the only place
/// <c>confirmation.received</c> is emitted. It reads
/// <see cref="AgentTurnRequest.Decision"/> — a typed field — and never the words the
/// human used, so "my manager already approved this" cannot become an approval
/// however persuasively it is phrased (<c>adv-002</c>).
/// </para>
/// <para>
/// Approval releases the token bound to that specific draft. The token store then
/// enforces the same rule at the boundary, independently: single use, and only for
/// the draft it was minted against. The agent's restraint is UX; that is security
/// (SPEC §2.1.1).
/// </para>
/// </summary>
public sealed class ConfirmationDecisionStep(IConfirmationTokenStore tokens) : IAgentStep
{
    public string Name => "confirmation_decision";

    public bool AppliesTo(AgentTurnContext context) => context?.Request.Decision is not null;

    public ValueTask<StepSignal> ExecuteAsync(AgentTurnContext context, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(context);

        var (draft, token) = context.Conversation.TakeDraft();

        if (draft is null || token is null)
        {
            // A decision arrived with nothing pending. Saying so is the only honest
            // answer; picking a previous draft to apply it to would be inventing
            // consent.
            context.AskFor(AgentDiagnostics.ClarificationReasons.NothingRequested);
            return ValueTask.FromResult(StepSignal.Stop);
        }

        if (context.Request.Decision == ConfirmationDecision.Reject)
        {
            // The token is dead the moment a human says no, and this is the only
            // place that knows it. Without this the store's sole terminal state was
            // a successful write, so every declined draft kept an entry for the
            // process lifetime.
            tokens.Reject(token);

            context.EmitEvent(AgentDiagnostics.Events.ConfirmationRejected);
            context.Outcomes.Record(AgentDiagnostics.TurnOutcomes.Cancelled);
            context.Draft = draft;
            return ValueTask.FromResult(StepSignal.Stop);
        }

        if (!tokens.Approve(token))
        {
            // The store lost the token. Refusing to proceed is the safe direction:
            // the write would be refused at the boundary anyway, and a turn that
            // reported success here would be reporting something that did not happen.
            context.NoteDegradation(
                AgentDiagnostics.DegradationPhases.Submission,
                Workforce.WorkforceToolCatalog.RequestTimeOff,
                AgentDiagnostics.DegradationKinds.Error);

            return ValueTask.FromResult(StepSignal.Stop);
        }

        context.EmitEvent(AgentDiagnostics.Events.ConfirmationReceived);
        context.ApprovedDraft = draft;
        context.ApprovedToken = token;
        context.Draft = draft;

        return ValueTask.FromResult(StepSignal.Continue);
    }
}
