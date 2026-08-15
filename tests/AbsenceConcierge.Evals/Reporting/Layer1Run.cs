using System.Diagnostics;
using System.Text.Json;
using AbsenceConcierge.AgentService.Agent.Language;
using AbsenceConcierge.Evals.Assertions;
using AbsenceConcierge.Evals.Execution;
using AbsenceConcierge.Evals.Scenarios;
using Microsoft.Extensions.DependencyInjection;

namespace AbsenceConcierge.Evals.Reporting;

/// <summary>
/// Runs the whole corpus once, and hands the same report to every test that needs it.
///
/// <para>
/// Once, because the suite has a three-minute budget on a pull request and running
/// thirty-two scenarios per assertion would spend it on repetition. The theory below
/// then reports one test per scenario, which is what makes a failure readable — a
/// single test called "the evals pass" tells you nothing except that they did not.
/// </para>
/// <para>
/// <b>There is no retry setting, and there will not be one.</b> The gated path is
/// deterministic by construction (SPEC §8.2): a rule-based interpreter, mock tools,
/// a pinned clock. n = 1, and a failure is a failure. A retried-until-green suite is
/// a false regression net — worse than none, because it is trusted.
/// </para>
/// </summary>
public static class Layer1Run
{
    private static readonly Lazy<EvalReport> Lazy = new(Execute, LazyThreadSafetyMode.ExecutionAndPublication);

    public static EvalReport Report => Lazy.Value;

    public static IReadOnlyList<LoadedScenario> Corpus { get; } = ScenarioLoader.LoadAll();

    /// <summary>Where Phase 6 will pick the report up from.</summary>
    public static string ReportPath => Path.Combine(RepositoryLayout.Root, "TestResults", "eval-report.json");

    private static EvalReport Execute()
    {
        var stopwatch = Stopwatch.StartNew();
        var results = Corpus.Select(scenario => RunOne(scenario)).ToList();
        stopwatch.Stop();

        var report = new EvalReport(
            Layer: 1,
            SpecVersion: AgentVersion(),
            Interpreter: DeterministicUtteranceInterpreter.InterpreterName,
            DurationMs: stopwatch.ElapsedMilliseconds,
            Scenarios: results);

        report.WriteTo(ReportPath);
        Console.WriteLine(report.Summarise());

        return report;
    }

    /// <summary>
    /// Runs one scenario, optionally with a step swapped out — which is what the
    /// mutation pass (SPEC §8.6) uses to prove the constraint layer can fail.
    /// </summary>
    public static ScenarioResult RunOne(LoadedScenario loaded, Action<IServiceCollection>? mutate = null)
    {
        ArgumentNullException.ThrowIfNull(loaded);

        var scenario = loaded.Scenario;

        if (scenario.Skip is { } skip)
        {
            // A skip with a reason, never a silent pass and never a deleted file.
            return new ScenarioResult(
                scenario.Id,
                scenario.Class,
                scenario.Gate,
                ScenarioStatus.SkippedUnimplemented,
                [],
                $"{skip.Reason} (since {skip.Since})",
                null,
                0);
        }

        var stopwatch = Stopwatch.StartNew();

        try
        {
            var run = ScenarioRunner.Execute(loaded, mutate);
            var outcomes = scenario.Expect
                .Select(assertion => AssertionEvaluator.Evaluate(assertion, run))
                .ToList();

            stopwatch.Stop();

            return new ScenarioResult(
                scenario.Id,
                scenario.Class,
                scenario.Gate,
                outcomes.All(outcome => outcome.Passed) ? ScenarioStatus.Pass : ScenarioStatus.Fail,
                outcomes,
                null,
                null,
                stopwatch.ElapsedMilliseconds);
        }
#pragma warning disable CA1031 // One broken scenario must not take the report with it.
        catch (Exception exception)
        {
            stopwatch.Stop();

            // Distinct from a failure on purpose. "The agent did the wrong thing" and
            // "the harness could not tell" are different findings, and collapsing them
            // sends someone to debug an agent that is fine.
            return new ScenarioResult(
                scenario.Id,
                scenario.Class,
                scenario.Gate,
                ScenarioStatus.Error,
                [],
                null,
                $"{exception.GetType().Name}: {exception.Message}",
                stopwatch.ElapsedMilliseconds);
        }
#pragma warning restore CA1031
    }

    /// <summary>
    /// The agent definition's version, which SPEC §10 moves with the spec version.
    /// Read rather than hardcoded, so a report can never claim to have measured a
    /// version of the contract that was not on disk when it ran.
    /// </summary>
    private static string AgentVersion()
    {
        var path = Path.Combine(RepositoryLayout.Root, "agents", "absence-concierge", "definition.json");
        using var document = JsonDocument.Parse(File.ReadAllText(path));

        return document.RootElement.TryGetProperty("version", out var version)
            ? version.GetString() ?? "unknown"
            : "unknown";
    }
}
