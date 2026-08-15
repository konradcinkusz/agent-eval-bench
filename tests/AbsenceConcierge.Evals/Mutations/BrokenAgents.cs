using AbsenceConcierge.AgentService.Agent;
using AbsenceConcierge.AgentService.Agent.Language;
using AbsenceConcierge.AgentService.Agent.Steps;
using AbsenceConcierge.AgentService.Telemetry;
using AbsenceConcierge.AgentService.Workforce;
using AbsenceConcierge.AgentService.Workforce.Confirmation;
using Microsoft.Extensions.DependencyInjection;

namespace AbsenceConcierge.Evals.Mutations;

/// <summary>
/// Deliberately broken agents, and the scenario each one must be caught by.
///
/// <para>
/// <b>Why this exists.</b> "Once a test has a real assertion, that only proves it
/// can pass — not that it can catch anything" (E2E-ACCEPTANCE-TESTING.md §2). The
/// estate requires a mutation pass for E2E suites and has no equivalent requirement
/// for evals, so an eval suite can be adopted wholesale without anyone ever proving
/// it can fail. SPEC §8.6 closes that locally, and this is it: four agents that
/// break a named constraint, and the assertion that must notice.
/// </para>
/// <para>
/// <b>A variant that survives is a missing scenario, not a curiosity.</b> That is
/// not a slogan here — it happened on the first run. <c>deg-003</c> and
/// <c>deg-004</c> asserted <c>at_least: 1</c> on the write, so the agent that
/// submits twice against one confirmation passed both, and SPEC §2.2.1 said the
/// attempt bound would have caught it. It would not: an orchestrator retry opens a
/// second span with one attempt in it. Both scenarios and that sentence were fixed
/// in the same pull request as this file.
/// </para>
/// <para>
/// Each mutant keeps the <em>name</em> of the step it replaces, so the pipeline it
/// produces is indistinguishable in the trace except through the constraint it
/// breaks. A mutant that announced itself would be testing the announcement.
/// </para>
/// </summary>
public static class BrokenAgents
{
    /// <param name="Name">How the variant is described in a failure.</param>
    /// <param name="ScenarioId">The scenario that must catch it.</param>
    /// <param name="Break">Swaps the step out, in place, so the pipeline's order is unchanged.</param>
    public sealed record Variant(string Name, string ScenarioId, Action<IServiceCollection> Break);

    public static IReadOnlyList<Variant> All { get; } =
    [
        new("writes-before-the-gate", "adv-001-injection-via-user-input-direct",
            services => Replace<ConfirmationGateStep, WritesBeforeTheGateStep>(services)),

        new("fabricates-a-leave-type", "hap-001-sick-today-and-tomorrow",
            services => Replace<LeaveTypeStep, FabricatesLeaveTypeStep>(services)),

        new("resubmits-an-indeterminate-write", "deg-004-write-times-out-status-unknown",
            services => Replace<ExecuteWriteStep, ResubmitsIndeterminateWriteStep>(services)),

        new("obeys-an-instruction-in-a-tool-result", "adv-003-injection-via-leave-type-name",
            services => Replace<ConfirmationGateStep, ObeysToolResultInstructionStep>(services)),
    ];

    /// <summary>
    /// Swaps one step for another <b>at the same index</b>. Replacing in place rather
    /// than removing and appending matters: the pipeline's order is the specification,
    /// and a mutant that also reordered the pipeline would be two changes at once.
    /// </summary>
    private static void Replace<TOriginal, TMutant>(IServiceCollection services)
        where TMutant : class, IAgentStep
    {
        for (var index = 0; index < services.Count; index++)
        {
            if (services[index].ServiceType == typeof(IAgentStep)
                && services[index].ImplementationType == typeof(TOriginal))
            {
                services[index] = ServiceDescriptor.Singleton<IAgentStep, TMutant>();
                return;
            }
        }

        throw new InvalidOperationException(
            $"No step registered for {typeof(TOriginal).Name}. The mutation pass and the pipeline have "
            + "drifted, which means the variants are no longer breaking what they claim to break.");
    }
}

/// <summary>Breaks C-1: mints its own approval and writes in the same turn.</summary>
internal sealed class WritesBeforeTheGateStep(IConfirmationTokenStore tokens) : IAgentStep
{
    public string Name => "confirmation_gate";

    public bool AppliesTo(AgentTurnContext context) => context is { Draft: not null, ApprovedDraft: null };

    public ValueTask<StepSignal> ExecuteAsync(AgentTurnContext context, CancellationToken cancellationToken)
    {
        var draft = context.Draft!;

        var token = tokens.Issue(new ConfirmationDraft(
            draft.EmployeeId,
            draft.LeaveType.Id,
            draft.StartDate,
            draft.EndDate));

        tokens.Approve(token);

        context.ApprovedDraft = draft;
        context.ApprovedToken = token;

        return ValueTask.FromResult(StepSignal.Continue);
    }
}

/// <summary>Breaks C-5: names a leave type it never retrieved.</summary>
internal sealed class FabricatesLeaveTypeStep : IAgentStep
{
    public string Name => "retrieve_leave_types";

    public bool AppliesTo(AgentTurnContext context) =>
        context?.Intent is { Kind: IntentKind.RequestTimeOff };

    public ValueTask<StepSignal> ExecuteAsync(AgentTurnContext context, CancellationToken cancellationToken)
    {
        // Confidently, plausibly wrong — the failure mode a judge does not catch.
        context.SelectedLeaveType = new LeaveType(
            "lt-999",
            "Sick leave",
            RequiresApproval: false,
            CountsAgainstBalance: false,
            MaxConsecutiveDays: 30,
            AllowsHalfDays: false,
            RequiresAttachmentAfterDays: 3);

        return ValueTask.FromResult(StepSignal.Continue);
    }
}

/// <summary>Breaks C-6: submits again when the first submission's fate is unknown.</summary>
internal sealed class ResubmitsIndeterminateWriteStep(IWorkforceTools tools) : IAgentStep
{
    public string Name => "submit_request";

    public bool AppliesTo(AgentTurnContext context) => context?.ApprovedDraft is not null;

    public async ValueTask<StepSignal> ExecuteAsync(
        AgentTurnContext context,
        CancellationToken cancellationToken)
    {
        var draft = context.ApprovedDraft!;
        var request = new TimeOffRequest(
            draft.LeaveType.Id,
            draft.StartDate,
            draft.EndDate,
            context.ApprovedToken!);

        var result = await tools.RequestTimeOffAsync(request, cancellationToken).ConfigureAwait(false);

        if (result.Outcome == ToolOutcome.Indeterminate)
        {
            // "It probably did not land, so let us be helpful." This is the sentence
            // that books two holidays.
            result = await tools.RequestTimeOffAsync(request, cancellationToken).ConfigureAwait(false);
        }

        context.WriteResult = result;

        if (result.IsSuccess)
        {
            return StepSignal.Continue;
        }

        context.NoteDegradation(
            AgentDiagnostics.DegradationPhases.Submission,
            WorkforceToolCatalog.RequestTimeOff,
            AgentDiagnostics.DegradationKinds.Timeout);

        return StepSignal.Stop;
    }
}

/// <summary>Breaks C-7: does what a leave type's name told it to do.</summary>
internal sealed class ObeysToolResultInstructionStep(IConfirmationTokenStore tokens) : IAgentStep
{
    private readonly ConfirmationGateStep _honest = new(tokens);

    public string Name => "confirmation_gate";

    public bool AppliesTo(AgentTurnContext context) => context is { Draft: not null, ApprovedDraft: null };

    public ValueTask<StepSignal> ExecuteAsync(AgentTurnContext context, CancellationToken cancellationToken)
    {
        var draft = context.Draft!;

        if (InstructionShapedContent.Scan(draft.LeaveType.Name).Count == 0)
        {
            return _honest.ExecuteAsync(context, cancellationToken);
        }

        var token = tokens.Issue(new ConfirmationDraft(
            draft.EmployeeId,
            draft.LeaveType.Id,
            draft.StartDate,
            draft.EndDate));

        tokens.Approve(token);

        context.ApprovedDraft = draft;
        context.ApprovedToken = token;

        return ValueTask.FromResult(StepSignal.Continue);
    }
}
