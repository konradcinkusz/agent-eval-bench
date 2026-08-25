using System.Diagnostics;
using System.Globalization;
using AbsenceConcierge.AgentService.Agent.Language;
using AbsenceConcierge.AgentService.Agent.Time;
using AbsenceConcierge.AgentService.Telemetry;
using AbsenceConcierge.AgentService.Workforce.Fixtures;
using Microsoft.Extensions.Options;

namespace AbsenceConcierge.AgentService.Agent;

public interface IAgentOrchestrator
{
    ValueTask<AgentTurnResult> RunTurnAsync(AgentTurnRequest request, CancellationToken cancellationToken = default);
}

/// <summary>
/// Runs one turn: opens the turn span, walks the step pipeline, resolves the single
/// outcome, and composes the reply.
///
/// <para>
/// It contains no domain reasoning. Every decision belongs to a step, and the
/// orchestrator's whole job is to run them in order, stop when one says stop, and
/// make sure the turn ends with exactly one recorded outcome and one recorded
/// termination reason. That is what makes the estate's comparison hold — 130 lines
/// for the service with a step pipeline against 399 for the one doing the same work
/// inline, and the second is the harder file to change.
/// </para>
/// </summary>
public sealed class AgentOrchestrator(
    IEnumerable<IAgentStep> steps,
    IAgentConversationStore conversations,
    IUtteranceInterpreter interpreter,
    IReplyComposer composer,
    WorkforceWorld world,
    TimeProvider timeProvider,
    IOptions<AgentOptions> options,
    ILogger<AgentOrchestrator> logger) : IAgentOrchestrator
{
    private readonly IReadOnlyList<IAgentStep> _steps = [.. steps];
    private readonly AgentOptions _options = options.Value;

    public async ValueTask<AgentTurnResult> RunTurnAsync(
        AgentTurnRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        var conversation = conversations.GetOrCreate(request.ConversationId);
        var turnIndex = conversation.NextTurn();

        // GenAI semantic conventions: the turn is an agent invocation, and the tool
        // calls beneath it are its children. One vocabulary for a production trace
        // and an offline scenario, so a live failure replays as a scenario by
        // extraction rather than by authorship (P15).
        using var activity = AgentDiagnostics.Source.StartActivity(
            $"invoke_agent {AgentIdentity.Slug}",
            ActivityKind.Internal);

        activity?.SetTag(AgentDiagnostics.Attributes.OperationName, "invoke_agent");
        activity?.SetTag(AgentDiagnostics.Attributes.AgentName, AgentIdentity.Slug);
        activity?.SetTag(AgentDiagnostics.Attributes.Interpreter, interpreter.Name);
        activity?.SetTag(AgentDiagnostics.Attributes.TurnIndex, turnIndex);

        var context = new AgentTurnContext(
            request,
            conversation,
            new AgentClock(timeProvider, ResolveZone()),
            WorkingCalendar.FromWorld(world),
            activity);

        var termination = AgentDiagnostics.TerminationReasons.Decision;
        var stepsRun = 0;
        IAgentStep? running = null;

        try
        {
            foreach (var step in _steps)
            {
                if (stepsRun >= _options.MaxSteps)
                {
                    // C-4. A loop that ends by exhaustion has no decision behind its
                    // last message, so the turn says so rather than presenting
                    // whatever it happened to be holding as an answer.
                    termination = AgentDiagnostics.TerminationReasons.IterationCap;
                    logger.LogError(
                        "Agent turn {TurnIndex} hit the step cap of {MaxSteps}. The pipeline is longer than the cap allows.",
                        turnIndex,
                        _options.MaxSteps);
                    break;
                }

                stepsRun++;
                running = step;

                if (await RunStepAsync(step, context, cancellationToken).ConfigureAwait(false) == StepSignal.Stop)
                {
                    break;
                }
            }
        }
        catch (OperationCanceledException)
        {
            throw;
        }
#pragma warning disable CA1031 // A turn that throws must still produce a graded outcome.
        catch (Exception exception)
        {
            // The alternative is an unhandled exception, which produces no outcome
            // attribute at all — and a scenario would then fail with "no outcome"
            // rather than with the failure that caused it.
            termination = AgentDiagnostics.TerminationReasons.Error;
            activity?.SetStatus(ActivityStatusCode.Error, exception.Message);
            logger.LogError(exception, "Agent turn {TurnIndex} failed.", turnIndex);

            // Without this, a turn that threw before any step recorded an outcome
            // resolved to the default — completed — and the composer answered
            // "That is done." to a request that did nothing (F-14). The guard on
            // WriteResult keeps the note honest in the other direction: if the
            // write had already happened when a later step threw, "completed" is
            // the truth and a sentence claiming nothing was submitted would not be.
            if (context.WriteResult is null)
            {
                context.Outcomes.Record(AgentDiagnostics.TurnOutcomes.Degraded);
                context.NoteDegradation(
                    AgentDiagnostics.DegradationPhases.Pipeline,
                    running?.Name ?? "unknown",
                    AgentDiagnostics.DegradationKinds.Error);
            }
        }
#pragma warning restore CA1031

        var outcome = context.Outcomes.Resolve();

        activity?.SetTag(AgentDiagnostics.Attributes.TurnOutcome, outcome);
        activity?.SetTag(AgentDiagnostics.Attributes.TerminationReason, termination);
        activity?.SetTag(AgentDiagnostics.Attributes.Iterations, stepsRun);

        // Composed here rather than as a final step, because rendering is not a
        // decision and must happen on every path — including the one where a step
        // said stop, and the one where the turn threw. A reply produced by a step
        // that a `Stop` skipped would leave the user with silence on exactly the
        // turns that most need an explanation.
        var reply = await composer.ComposeAsync(context, outcome, cancellationToken).ConfigureAwait(false);

        return new AgentTurnResult(
            outcome,
            termination,
            reply,
            CardFor(context, outcome),
            context.Degradations.Count == 0 ? null : context.Degradations);
    }

    /// <summary>
    /// The draft, structured, on every turn that is still holding one and still
    /// waiting for an answer about it.
    ///
    /// <para>
    /// This was keyed to the <c>confirmation_pending</c> outcome, which withheld the
    /// card from the one path §7 rule 5 exists to describe. A read that fails before
    /// the gate annotates the draft rather than cancelling it, and outcome
    /// precedence then resolves that turn as <c>degraded</c> (deg-002). The composer
    /// asks <b>"Shall I submit it?"</b> on the condition below — a draft held and not
    /// yet approved — so the page rendered the question with no buttons to answer
    /// it. Typing "yes" is not an answer either: the decision travels as the typed
    /// field the buttons produce, so the visitor was asked something they had no way
    /// to say. The eval suite could not see it, because deg-002 asserts trace events
    /// and the card is part of the HTTP result.
    /// </para>
    /// <para>
    /// Both withholdings that were right are kept. <see cref="AgentTurnContext.Draft"/>
    /// is still set on the turn that executes the write, and a card there would let
    /// a client render "approve this?" beside a request already submitted —
    /// <c>ApprovedDraft</c> separates those. And the rejection turn sets the draft
    /// back on the context with no approval, so it is excluded by outcome: offering
    /// the buttons again to someone who has just pressed one of them is the same
    /// defect pointing the other way.
    /// </para>
    /// </summary>
    private static ConfirmationCard? CardFor(AgentTurnContext context, string outcome)
    {
        if (context is not { Draft: { } draft, ApprovedDraft: null }
            || string.Equals(outcome, AgentDiagnostics.TurnOutcomes.Cancelled, StringComparison.Ordinal))
        {
            return null;
        }

        return new ConfirmationCard(
            draft.LeaveType.Name,
            draft.StartDate.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
            draft.EndDate.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
            draft.WorkingDays,
            [.. draft.ExcludedDays.Select(day => string.Create(
                CultureInfo.InvariantCulture,
                $"{day.Date:yyyy-MM-dd} — {day.Label ?? day.Reason}"))],
            draft.AttachmentRequired,
            draft.ConflictCheck);
    }

    private static async ValueTask<StepSignal> RunStepAsync(
        IAgentStep step,
        AgentTurnContext context,
        CancellationToken cancellationToken)
    {
        using var activity = AgentDiagnostics.Source.StartActivity(
            $"agent_step {step.Name}",
            ActivityKind.Internal);

        activity?.SetTag(AgentDiagnostics.Attributes.StepName, step.Name);

        if (!step.AppliesTo(context))
        {
            activity?.SetTag(AgentDiagnostics.Attributes.StepApplied, false);
            return StepSignal.Continue;
        }

        activity?.SetTag(AgentDiagnostics.Attributes.StepApplied, true);
        return await step.ExecuteAsync(context, cancellationToken).ConfigureAwait(false);
    }

    private TimeZoneInfo ResolveZone() => AgentClock.ZoneFor(_options.Timezone);
}

/// <summary>Who the agent is, as the definition file names it.</summary>
public static class AgentIdentity
{
    public const string Slug = "absence-concierge";
}
