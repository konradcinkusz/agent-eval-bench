using System.Diagnostics;
using System.Globalization;
using AbsenceConcierge.AgentService.Agent;
using AbsenceConcierge.AgentService.Extensions;
using AbsenceConcierge.AgentService.Telemetry;
using AbsenceConcierge.AgentService.Workforce;
using AbsenceConcierge.AgentService.Workforce.Confirmation;
using AbsenceConcierge.AgentService.Workforce.Fixtures;
using AbsenceConcierge.Evals.Scenarios;
using AbsenceConcierge.Evals.World;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using OpenTelemetry;
using OpenTelemetry.Trace;

namespace AbsenceConcierge.Evals.Execution;

/// <summary>A clock that does not move, pinned to the scenario's instant.</summary>
internal sealed class PinnedClock(DateTimeOffset now) : TimeProvider
{
    public override DateTimeOffset GetUtcNow() => now;
}

/// <summary>What one scenario produced: the trace, and the world it ran against.</summary>
public sealed record ScenarioRun(
    TraceRecording Trace,
    WorkforceWorld World,
    IReadOnlyCollection<string> PermissionVocabulary);

/// <summary>
/// Runs one scenario end to end and hands back its trace.
///
/// <para>
/// It builds the agent <b>through the real composition root</b>
/// (<c>AddAbsenceConciergeAgent</c>) rather than assembling the step pipeline
/// itself. That is deliberate: the pipeline's order is the specification, and a
/// harness that built its own list would keep passing after somebody reordered the
/// registrations — the suite would be grading a pipeline the deployed service does
/// not have. What it substitutes is only the world, the clock and the tool chain,
/// which is exactly what a scenario is.
/// </para>
/// <para>
/// Nothing survives between scenarios: a fresh service provider, a fresh token
/// store, a fresh conversation store, a world rebuilt from the fixture (SPEC §8.3).
/// </para>
/// </summary>
public static class ScenarioRunner
{
    private const string ScopeName = "AbsenceConcierge.Evals.Scope";

    private static readonly ActivitySource Scope = new(ScopeName);

    public static ScenarioRun Execute(LoadedScenario loaded, Action<IServiceCollection>? mutate = null)
    {
        ArgumentNullException.ThrowIfNull(loaded);

        var scenario = loaded.Scenario;
        var world = FixtureComposer.Compose(loaded);
        var clock = ParseClock(scenario.Fixture.Clock, loaded.Id);

        var captured = new List<Activity>();
        var traces = new HashSet<ActivityTraceId>();

        using var tracer = Sdk.CreateTracerProviderBuilder()
            .AddSource(AgentDiagnostics.ActivitySourceName)
            .AddSource(ScopeName)
            .AddInMemoryExporter(captured)
            .Build()!;

        using var provider = BuildProvider(scenario, world, clock, mutate);
        var orchestrator = provider.GetRequiredService<IAgentOrchestrator>();

        var turns = new List<TurnRecord>();

        for (var index = 0; index < scenario.Conversation.Count; index++)
        {
            var turn = scenario.Conversation[index];

            using var scope = Scope.StartActivity("scenario_turn")
                ?? throw new InvalidOperationException(
                    "The scenario scope span was not sampled, so the trace could not be attributed "
                    + "to this run. Every assertion below would read an empty trace and pass.");

            traces.Add(scope.TraceId);

            var result = orchestrator
                .RunTurnAsync(ToRequest(loaded.Id, turn))
                .AsTask()
                .GetAwaiter()
                .GetResult();

            turns.Add(new TurnRecord(index + 1, result.Outcome, result.TerminationReason, result.Reply));
        }

        tracer.ForceFlush();

        var mine = captured.Where(activity =>
            traces.Contains(activity.TraceId)
            && !string.Equals(activity.Source.Name, ScopeName, StringComparison.Ordinal));

        return new ScenarioRun(
            TraceRecording.From(mine, turns),
            world,
            FixtureComposer.PermissionVocabulary(loaded));
    }

    private static ServiceProvider BuildProvider(
        ScenarioFile scenario,
        WorkforceWorld world,
        DateTimeOffset clock,
        Action<IServiceCollection>? mutate)
    {
        var services = new ServiceCollection();

        services.AddLogging(logging => logging.SetMinimumLevel(LogLevel.Warning));
        services.AddAbsenceConciergeAgent(new ConfigurationBuilder().Build());

        // After AddAbsenceConciergeAgent, so the scenario's zone and locale win over
        // the section binding. A scenario that resolved dates in the default zone
        // would be testing the default rather than itself — and the locale is
        // injected for the same reason: fixture.locale is what selects which
        // language reads the utterance (SPEC §9), and a runner that ignored it
        // would leave every Spanish scenario secretly graded in English.
        services.PostConfigure<AgentOptions>(options =>
        {
            options.Timezone = scenario.Fixture.Timezone;

            if (!string.IsNullOrWhiteSpace(scenario.Fixture.Locale))
            {
                options.Locale = scenario.Fixture.Locale;
            }
        });

        var time = new PinnedClock(clock);
        var tokens = new InMemoryConfirmationTokenStore();

        services.AddSingleton<TimeProvider>(time);
        services.AddSingleton(world);
        services.AddSingleton<IConfirmationTokenStore>(tokens);

        services.AddSingleton<IWorkforceTools>(sp => WorkforceToolsFactory.Build(
            world,
            tokens,
            time,
            sp.GetRequiredService<IOptions<AgentOptions>>().Value.MaxReadAttempts,
            scenario.Fixture.ToolBehaviour.Count == 0
                ? null
                : inner => new FaultInjectingWorkforceTools(inner, scenario.Fixture.ToolBehaviour)));

        // The mutation pass (SPEC §8.6) swaps a step here to prove the constraint
        // layer can catch a broken agent. Nothing else uses it.
        mutate?.Invoke(services);

        return services.BuildServiceProvider();
    }

    private static AgentTurnRequest ToRequest(string conversationId, ScenarioTurn turn) =>
        turn.Role switch
        {
            "user" => AgentTurnRequest.User(conversationId, turn.Content),

            "confirmation" => AgentTurnRequest.Confirmation(
                conversationId,
                turn.Content,
                turn.Decision switch
                {
                    "approve" => ConfirmationDecision.Approve,
                    "reject" => ConfirmationDecision.Reject,
                    _ => throw new InvalidOperationException(
                        $"Confirmation turn has decision '{turn.Decision}', which is neither approve nor reject."),
                }),

            _ => throw new InvalidOperationException($"Unknown conversation role '{turn.Role}'."),
        };

    private static DateTimeOffset ParseClock(string clock, string scenarioId) =>
        DateTimeOffset.TryParse(
            clock,
            CultureInfo.InvariantCulture,
            DateTimeStyles.RoundtripKind,
            out var parsed)
            ? parsed
            : throw new InvalidOperationException(
                $"Scenario '{scenarioId}' has an unparseable clock: '{clock}'. Every scenario pins one, "
                + "because a suite whose result depends on the day it runs is not a suite.");
}
