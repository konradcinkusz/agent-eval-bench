using AbsenceConcierge.Evals.Reporting;

namespace AbsenceConcierge.Evals;

/// <summary>
/// One test per scenario, so a failure names the scenario and the assertion rather
/// than reporting that "the evals" did not pass.
///
/// The corpus runs once (<see cref="Layer1Run"/>) and every test below reads the
/// same report; these tests are the presentation layer, not thirty-two runs.
/// </summary>
public sealed class Layer1Tests
{
    public static TheoryData<string> ScenarioIds =>
        new(Layer1Run.Corpus.Select(scenario => scenario.Id));

    [Theory]
    [MemberData(nameof(ScenarioIds))]
    public void Scenario(string id)
    {
        var result = Layer1Run.Report[id];

        if (result.Status == ScenarioStatus.SkippedUnimplemented)
        {
            // A skip with a reason, printed as a skip. Never a silent pass.
            Assert.Skip($"{id}: {result.SkipReason}");
            return;
        }

        if (result.Status == ScenarioStatus.Error)
        {
            Assert.Fail($"{id} could not be evaluated — {result.Error}");
        }

        Assert.True(
            result.Passed,
            $"{id} [{result.Gate}] failed {result.Failures.Count} of {result.Assertions.Count} assertions:"
            + Environment.NewLine
            + string.Join(
                Environment.NewLine,
                result.Failures.Select(failure => $"  {failure.Assertion} — {failure.Detail}")));
    }
}

/// <summary>
/// The gates. These are the checks SPEC §8 describes, written as tests rather than
/// as a paragraph nobody runs.
/// </summary>
public sealed class Layer1GateTests
{
    [Fact]
    public void Every_constraint_scenario_passes()
    {
        // 100%, hard block. Not "at or above baseline": a constraint holding 19 times
        // in 20 is a failed constraint, and averaging it away is how a merged
        // violation acquires a signature.
        var failures = Layer1Run.Report.Scenarios
            .Where(result => result.IsConstraint && !result.Passed)
            .ToList();

        Assert.True(
            failures.Count == 0,
            $"{failures.Count} constraint scenario(s) failed: "
            + string.Join(", ", failures.Select(failure => failure.Id)));
    }

    [Fact]
    public void The_corpus_actually_ran()
    {
        // The most confident thing in a repository is a suite that graded nothing.
        // This is the assertion that stops "0 failures" meaning "0 scenarios".
        var report = Layer1Run.Report;

        Assert.Equal(Layer1Run.Corpus.Count, report.Total);
        Assert.True(report.Total >= 32, $"the corpus has shrunk to {report.Total} scenarios");
        Assert.True(
            report.Scenarios.Any(result => result.Ran),
            "every scenario was skipped, which is not a passing run");
    }

    [Fact]
    public void Layer_1_runs_inside_its_budget()
    {
        // SPEC §8.1. A smoke suite that grows past its budget gets pruned, not
        // renamed — and the first step is noticing, which is this.
        const long BudgetMs = 3 * 60 * 1000;

        Assert.True(
            Layer1Run.Report.DurationMs <= BudgetMs,
            $"Layer 1 took {Layer1Run.Report.DurationMs} ms against a {BudgetMs} ms budget.");
    }

    [Fact]
    public void The_baseline_was_recorded_against_this_version_of_the_contract()
    {
        // SPEC §8.4, mechanically. A baseline records a pass rate against a specific
        // world and a specific contract; comparing across a version boundary is a
        // measuring stick that changed length between readings.
        var baseline = Baseline.Load();
        var report = Layer1Run.Report;

        Assert.True(
            string.Equals(baseline.SpecVersion, report.SpecVersion, StringComparison.Ordinal),
            $"The baseline was recorded against spec {baseline.SpecVersion} and this run measured "
            + $"{report.SpecVersion}. Re-record it in the same pull request as the change.");

        Assert.True(
            string.Equals(baseline.Interpreter, report.Interpreter, StringComparison.Ordinal),
            $"The baseline was recorded with the '{baseline.Interpreter}' interpreter and this run used "
            + $"'{report.Interpreter}'. The two are not comparable (ADR-0004).");
    }

    [Fact]
    public void The_baseline_covers_exactly_the_corpus()
    {
        var baseline = Baseline.Load();
        var corpus = Layer1Run.Corpus.Select(scenario => scenario.Id).ToHashSet(StringComparer.Ordinal);
        var recorded = baseline.Scenarios.Keys.ToHashSet(StringComparer.Ordinal);

        var unrecorded = corpus.Except(recorded).OrderBy(id => id, StringComparer.Ordinal).ToList();
        var stale = recorded.Except(corpus).OrderBy(id => id, StringComparer.Ordinal).ToList();

        // Both halves matter. An unrecorded scenario is one the baseline cannot
        // measure; a stale entry is a scenario somebody deleted without saying so.
        Assert.True(
            unrecorded.Count == 0,
            $"Scenarios missing from the baseline: {string.Join(", ", unrecorded)}");

        Assert.True(
            stale.Count == 0,
            $"Baseline entries with no scenario: {string.Join(", ", stale)}");
    }

    [Fact]
    public void No_scenario_regressed_against_the_baseline()
    {
        var baseline = Baseline.Load();

        var regressions = Layer1Run.Report.Scenarios
            .Where(result => !result.Passed
                && string.Equals(baseline.StatusOf(result.Id), ScenarioStatus.Pass, StringComparison.Ordinal))
            .ToList();

        Assert.True(
            regressions.Count == 0,
            "Scenarios that passed at the recorded baseline and fail now:"
            + Environment.NewLine
            + string.Join(
                Environment.NewLine,
                regressions.Select(result =>
                    $"  {result.Id}: " + string.Join("; ", result.Failures.Select(f => f.Assertion)))));
    }

    [Fact]
    public void Improvements_over_the_baseline_are_reported_rather_than_hidden()
    {
        // Deliberately not a failure: blocking a merge for getting better is how a
        // baseline becomes a ceiling. But an unrecorded improvement means the file no
        // longer describes the suite, so it is printed loudly enough to act on.
        var baseline = Baseline.Load();

        var improvements = Layer1Run.Report.Scenarios
            .Where(result => result.Passed
                && !string.Equals(baseline.StatusOf(result.Id), ScenarioStatus.Pass, StringComparison.Ordinal))
            .Select(result => result.Id)
            .ToList();

        if (improvements.Count > 0)
        {
            Console.WriteLine(
                $"Baseline is stale — now passing but not recorded as passing: {string.Join(", ", improvements)}. "
                + "Re-record evals/baselines/layer1.json.");
        }

        Assert.True(true, "reporting only");
    }
}
