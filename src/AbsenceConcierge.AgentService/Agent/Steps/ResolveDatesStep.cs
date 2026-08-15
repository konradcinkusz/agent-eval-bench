using AbsenceConcierge.AgentService.Agent.Language;
using AbsenceConcierge.AgentService.Agent.Time;
using AbsenceConcierge.AgentService.Telemetry;

namespace AbsenceConcierge.AgentService.Agent.Steps;

/// <summary>
/// Resolves the user's date expression against the injected clock, in the actor's
/// timezone, and refuses to guess when it cannot.
///
/// <para>
/// The arithmetic is in <see cref="RelativeDateResolver"/> and is unit-tested there.
/// What lives here is the <em>policy</em>: which unresolved states are worth a
/// question, and which resolved ones are still worth one. Three are:
/// </para>
/// <list type="bullet">
///   <item>an expression with two defensible readings (B-12);</item>
///   <item>a date entirely on non-working days — a Saturday, or a company holiday —
///     because booking leave for a day nobody works is almost never what was
///     meant, and silently shifting it is worse;</item>
///   <item>a date in the past, which the tool boundary would refuse anyway.</item>
/// </list>
/// <para>
/// Splitting arithmetic from policy is what lets the first be exhaustively tested
/// without a sentence and the second be read as a list of decisions.
/// </para>
/// </summary>
public sealed class ResolveDatesStep : IAgentStep
{
    public string Name => "resolve_dates";

    public bool AppliesTo(AgentTurnContext context) => context?.Intent is { Kind: IntentKind.RequestTimeOff };

    public ValueTask<StepSignal> ExecuteAsync(AgentTurnContext context, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(context);

        var expression = context.Intent!.Dates;

        if (expression is null)
        {
            return Ask(context, AgentDiagnostics.ClarificationReasons.NoDateGiven);
        }

        var today = context.Clock.Today;
        var resolution = RelativeDateResolver.Resolve(expression, today);
        context.Dates = resolution;

        if (!resolution.IsResolved)
        {
            return Ask(context, AgentDiagnostics.ClarificationReasons.AmbiguousDate);
        }

        var start = resolution.Start!.Value;
        var end = resolution.End!.Value;

        if (start < today)
        {
            return Ask(context, AgentDiagnostics.ClarificationReasons.DateInThePast);
        }

        var count = context.Calendar.Count(start, end);

        if (count.WorkingDays == 0)
        {
            return Ask(context, AgentDiagnostics.ClarificationReasons.NonWorkingDay);
        }

        return ValueTask.FromResult(StepSignal.Continue);
    }

    private static ValueTask<StepSignal> Ask(AgentTurnContext context, string reason)
    {
        context.AskFor(reason);
        return ValueTask.FromResult(StepSignal.Stop);
    }
}
