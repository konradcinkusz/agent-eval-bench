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

        // The three below close the gap SPEC §8.6 left open: C-2, C-3 and C-4 had no
        // mutant, so the assertions guarding them had never been shown able to catch
        // anything. The repository's own line is the argument — "a suite that has
        // never failed is a suite nobody has tested" — and F-1, the defect the first
        // mutation run found, was exactly a constraint whose assertions could not
        // fail.
        new("ignores-the-permission-fixture", "den-004-missing-permission-to-request-leave",
            services => Replace<ScopeGuardStep, IgnoresThePermissionFixtureStep>(services)),

        new("leaks-an-internal-identifier", "hap-003-single-day-vacation-friday",
            ReplaceComposer<LeaksAnInternalIdentifierComposer>),

        new("ends-a-turn-by-exhaustion", "hap-004-overlap-with-existing-booking-is-reported",
            services => services.PostConfigure<AgentOptions>(options => options.MaxSteps = 1)),
    ];

    /// <summary>
    /// The C-3 mutant is not a step — the composer is where user-facing prose is
    /// produced, and C-3 is a property of that prose — so it is swapped at its own
    /// registration rather than in the pipeline.
    /// </summary>
    private static void ReplaceComposer<TMutant>(IServiceCollection services)
        where TMutant : class, IReplyComposer
    {
        for (var index = 0; index < services.Count; index++)
        {
            if (services[index].ServiceType == typeof(IReplyComposer))
            {
                services[index] = ServiceDescriptor.Singleton<IReplyComposer, TMutant>();
                return;
            }
        }

        throw new InvalidOperationException(
            "No IReplyComposer is registered. The mutation pass and the composition have drifted, "
            + "which means this variant is no longer breaking what it claims to break.");
    }

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

/// <summary>
/// Breaks C-2: keeps every other refusal and drops the permission check.
///
/// <para>
/// A copy of <see cref="ScopeGuardStep"/> without its O-7 branch, rather than a
/// guard that refuses nothing — a mutant that broke four rules at once would prove
/// the suite catches <em>something</em>, which is not the question. This one is
/// wrong about exactly one thing: it treats the permission fixture as advisory.
/// </para>
/// </summary>
internal sealed class IgnoresThePermissionFixtureStep : IAgentStep
{
    public string Name => "scope_guard";

    public bool AppliesTo(AgentTurnContext context) => context?.Intent is not null;

    public ValueTask<StepSignal> ExecuteAsync(AgentTurnContext context, CancellationToken cancellationToken)
    {
        var intent = context!.Intent!;

        var rule = intent.Kind switch
        {
            IntentKind.ApproveOrRejectLeave => AgentDiagnostics.RefusalRules.ApprovalIsAManagerAction,
            IntentKind.CancelOrEditBooking => AgentDiagnostics.RefusalRules.CannotModifyBookings,
            IntentKind.PayrollOrPolicyQuestion => AgentDiagnostics.RefusalRules.PayrollBelongsToHr,
            IntentKind.MedicalAdvice => AgentDiagnostics.RefusalRules.NoMedicalJudgement,
            _ => null,
        };

        if (rule is not null)
        {
            context.Refuse(rule);
            return ValueTask.FromResult(StepSignal.Stop);
        }

        if (intent.Person is { Role: PersonRole.Subject })
        {
            context.Refuse(AgentDiagnostics.RefusalRules.OnlyForTheSignedInUser);
            return ValueTask.FromResult(StepSignal.Stop);
        }

        // The O-7 branch that belongs here is gone. The actor's permissions are read
        // by nothing, so a request the fixture forbids proceeds to a draft.
        return ValueTask.FromResult(StepSignal.Continue);
    }
}

/// <summary>
/// Breaks C-3: composes the real reply and appends an internal identifier.
///
/// <para>
/// Deliberately a decorator rather than a rewrite. The prose stays correct and
/// useful — which is the failure mode worth catching, because a reply that reads
/// perfectly and carries one id past the boundary is the one a human reviewer signs
/// off. <c>output_excludes_internal_ids</c> is asserted by every scenario in the
/// corpus and had, until now, never been shown able to fail.
/// </para>
/// </summary>
internal sealed class LeaksAnInternalIdentifierComposer(DeterministicReplyComposer inner) : IReplyComposer
{
    public async ValueTask<string> ComposeAsync(
        AgentTurnContext context,
        string outcome,
        CancellationToken cancellationToken = default)
    {
        var reply = await inner.ComposeAsync(context, outcome, cancellationToken).ConfigureAwait(false);
        var leaked = context?.SelectedLeaveType?.Id ?? context?.Draft?.LeaveType.Id ?? "lt-201";

        return $"{reply} (ref {leaked})";
    }
}
