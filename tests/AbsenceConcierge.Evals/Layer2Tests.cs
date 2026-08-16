using AbsenceConcierge.Evals.Judging;
using AbsenceConcierge.Evals.Reporting;
using AbsenceConcierge.Evals.Scenarios;

namespace AbsenceConcierge.Evals;

/// <summary>
/// Layer 2's gates, and the checks that keep it honest when it cannot run.
///
/// <para>
/// Without a credential every scenario here reports <c>skipped:no-credential</c>.
/// That is legitimate on a pull request and nowhere else, so the tests below do two
/// things: they gate the scores when there are scores, and they assert that the
/// keyed nightly path <em>exists</em> when there are not. A judge that skips on every
/// pull request and every nightly is the config no CI context ever executes.
/// </para>
/// </summary>
public sealed class Layer2Tests
{
    [Fact]
    public void Every_rubric_meets_its_threshold()
    {
        var report = Layer2Run.Report;

        if (!report.Ran)
        {
            Assert.Skip(
                $"skipped:no-credential — {report.SkippedNoCredential} scenario(s). {report.Calibration.Reason}");

            return;
        }

        var below = report.Rubrics.Where(rubric => !rubric.MeetsThreshold).ToList();

        Assert.True(
            below.Count == 0,
            "Rubrics below threshold: "
            + string.Join(", ", below.Select(rubric => $"{rubric.Rubric} {rubric.Mean:F2} < {rubric.Threshold:F2}")));
    }

    [Fact]
    public void No_single_score_falls_through_a_floor()
    {
        var report = Layer2Run.Report;

        if (!report.Ran)
        {
            Assert.Skip("skipped:no-credential");
            return;
        }

        // grounding is the criterion with a floor, and the reason is worth keeping in
        // view: a mean of 2.5 can be reached by two perfect answers and one that
        // asserted something no tool returned. The mean says the agent is usually
        // grounded; the floor is what says it was never confidently wrong.
        var breached = report.Rubrics.Where(rubric => !rubric.MeetsFloor).ToList();

        Assert.True(
            breached.Count == 0,
            "Rubrics with a score below their floor: "
            + string.Join(", ", breached.Select(rubric => $"{rubric.Rubric} lowest {rubric.Lowest} < {rubric.Floor}")));
    }

    [Fact]
    public void A_judge_that_could_not_be_read_is_reported_as_such()
    {
        // Distinct from a low score, and it must never be averaged into one: an
        // unparseable verdict is a run that did not measure, and letting it stand in
        // as a zero would move a threshold on the strength of a number nobody produced.
        var broken = Layer2Run.Report.Scenarios
            .Where(scenario => scenario.Status == ScenarioStatus.Error)
            .ToList();

        Assert.True(
            broken.Count == 0,
            "The judge could not be read for: "
            + string.Join("; ", broken.Select(scenario => $"{scenario.Id} — {scenario.Error}")));
    }

    [Fact]
    public void Layer_2_runs_inside_its_budget()
    {
        // SPEC §8.1: two minutes and fifty cents per pull request. Cost is only
        // checked when rates are configured — tokens are the fact, dollars need an
        // input this repository does not own.
        var report = Layer2Run.Report;

        if (!report.Ran)
        {
            Assert.Skip("skipped:no-credential");
            return;
        }

        const long BudgetMs = 2 * 60 * 1000;
        const decimal BudgetUsd = 0.50m;

        Assert.True(
            report.DurationMs <= BudgetMs,
            $"Layer 2 took {report.DurationMs} ms against a {BudgetMs} ms budget.");

        if (report.EstimatedCostUsd is { } cost)
        {
            Assert.True(cost <= BudgetUsd, $"Layer 2 cost about ${cost:F4} against a ${BudgetUsd:F2} budget.");
        }
    }

    [Fact]
    public void The_report_says_which_judge_produced_it()
    {
        // AI-EVALS.md §5 pins the judge by model and prompt. The model comes back
        // from the service at run time; the prompt and the rubrics are pinned here,
        // as hashes, so an edited criterion is visible in the diff of a report rather
        // than invisible in the diff of a score.
        var report = Layer2Run.Report;

        Assert.Equal(12, report.RubricsHash.Length);
        Assert.Equal(12, report.PromptHash.Length);
        Assert.False(string.IsNullOrWhiteSpace(report.JudgeVersion));
    }

    [Fact]
    public void Judge_scores_gate_nothing_until_calibration_says_they_may()
    {
        // The honest state, reported rather than hidden. AI-EVALS.md §5 requires
        // calibration against human labels before a judge's scores gate anything, and
        // marks the rule "not yet demonstrated in the estate". This repository is the
        // demonstration, and today the demonstration is of the uncalibrated state:
        // 45 labels exist, but every one is an AI rater's and §5 says human — so the
        // scores are reported and trended and gate nothing.
        var calibration = Layer2Run.Report.Calibration;

        Console.WriteLine($"Calibration: {calibration.Reason}");
        Console.WriteLine(
            $"  labels {calibration.Labels}, scenarios {calibration.Scenarios}, compared {calibration.Compared}, "
            + $"exact {calibration.ExactAgreements}, within one {calibration.WithinOne}, "
            + $"κ {(calibration.Kappa is { } kappa ? kappa.ToString("F2", System.Globalization.CultureInfo.InvariantCulture) : "n/a")}");

        var gate = JudgeConfiguration.Current.Calibration;

        Assert.True(
            calibration.Gating || calibration.Labels < gate.MinimumLabels || calibration.Kappa is null
                || calibration.Kappa < gate.MinimumKappa,
            "The calibration report says the judge may not gate, but every stated condition is met. "
            + "That is a bug in the gate, not a finding about the judge.");
    }
}

/// <summary>
/// Checks on the judge's own definition, which cost nothing and run everywhere.
/// </summary>
public sealed class JudgeDefinitionTests
{
    [Fact]
    public void Every_rubric_a_scenario_names_is_defined()
    {
        var configuration = JudgeConfiguration.Current;

        var named = Layer1Run.Corpus
            .SelectMany(scenario => scenario.Scenario.Rubrics)
            .Distinct(StringComparer.Ordinal)
            .OrderBy(name => name, StringComparer.Ordinal)
            .ToList();

        var missing = named.Where(name => !configuration.Rubrics.ContainsKey(name)).ToList();

        Assert.True(
            missing.Count == 0,
            $"Scenarios name rubrics judge.yaml does not define: {string.Join(", ", missing)}");
    }

    [Fact]
    public void Every_rubric_that_is_defined_is_used_by_something()
    {
        // The other direction. A criterion nobody scores is a criterion nobody has
        // read since it was written, and it will be wrong by the time somebody does.
        var configuration = JudgeConfiguration.Current;

        var used = Layer1Run.Corpus
            .SelectMany(scenario => scenario.Scenario.Rubrics)
            .ToHashSet(StringComparer.Ordinal);

        var unused = configuration.Rubrics.Keys.Where(name => !used.Contains(name)).ToList();

        Assert.True(unused.Count == 0, $"Rubrics defined but never used: {string.Join(", ", unused)}");
    }

    [Fact]
    public void The_pull_request_subset_names_real_scenarios_that_carry_rubrics()
    {
        var configuration = JudgeConfiguration.Current;
        var corpus = Layer1Run.Corpus.ToDictionary(scenario => scenario.Id, StringComparer.Ordinal);

        foreach (var entry in configuration.Smoke)
        {
            Assert.True(corpus.ContainsKey(entry.Id), $"Smoke subset names '{entry.Id}', which does not exist.");

            Assert.True(
                corpus[entry.Id].Scenario.Rubrics.Count > 0,
                $"Smoke subset names '{entry.Id}', which carries no rubrics and so cannot be judged.");

            Assert.False(
                string.IsNullOrWhiteSpace(entry.Why),
                $"Smoke subset entry '{entry.Id}' has no reason. Spending money on every pull request is "
                + "a judgement, and a judgement with no reason cannot be argued with.");
        }
    }

    [Fact]
    public void The_pull_request_subset_covers_every_rubric()
    {
        // Otherwise a criterion is measured only nightly while appearing to gate on
        // every pull request — which is exactly the sort of half-wired gate that
        // reads as coverage and provides none.
        var configuration = JudgeConfiguration.Current;
        var corpus = Layer1Run.Corpus.ToDictionary(scenario => scenario.Id, StringComparer.Ordinal);

        var covered = configuration.Smoke
            .Where(entry => corpus.ContainsKey(entry.Id))
            .SelectMany(entry => corpus[entry.Id].Scenario.Rubrics)
            .ToHashSet(StringComparer.Ordinal);

        var uncovered = configuration.Rubrics.Keys.Where(name => !covered.Contains(name)).ToList();

        Assert.True(
            uncovered.Count == 0,
            $"Rubrics no pull-request scenario exercises: {string.Join(", ", uncovered)}");
    }

    [Fact]
    public void The_prompt_carries_the_anchors_it_is_given_and_nothing_else()
    {
        var configuration = JudgeConfiguration.Current;
        var prompt = configuration.BuildPrompt(["tone"], "TRANSCRIPT HERE");

        Assert.Contains("`tone`", prompt, StringComparison.Ordinal);
        Assert.Contains("not an occasion for enthusiasm", prompt, StringComparison.Ordinal);
        Assert.Contains("TRANSCRIPT HERE", prompt, StringComparison.Ordinal);

        // A criterion that was not asked for must not leak in. The judge is told to
        // score only what it is given, and giving it more is the fastest way to make
        // it score more.
        Assert.DoesNotContain("degradation-honesty", prompt, StringComparison.Ordinal);
        Assert.DoesNotContain("{{", prompt, StringComparison.Ordinal);
    }
}

/// <summary>
/// The claim that the keyed run exists, checked against the repository rather than
/// believed.
/// </summary>
public sealed class NightlyWorkflowTests
{
    private static string Workflow =>
        File.ReadAllText(Path.Combine(RepositoryLayout.Root, ".github", "workflows", "nightly.yml"));

    [Fact]
    public void A_nightly_workflow_exists_and_runs_the_full_set()
    {
        // SPEC §8.5: `skipped:no-credential` is legitimate on a pull request, and NOT
        // as the only outcome that ever occurs. The nightly keyed run is what keeps
        // Layer 2 honest, which makes its existence a property of the repository worth
        // asserting rather than a plan worth remembering.
        var workflow = Workflow;

        Assert.Contains("schedule:", workflow, StringComparison.Ordinal);
        Assert.Contains("EVAL_LAYER2_SCOPE: full", workflow, StringComparison.Ordinal);
        Assert.Contains("Llm__JudgeModel", workflow, StringComparison.Ordinal);
        Assert.Contains("secrets.", workflow, StringComparison.Ordinal);
    }

    [Fact]
    public void The_nightly_workflow_holds_no_secret_of_its_own()
    {
        // Every credential arrives from an environment secret. A value in the file is
        // a value in the history, and this repository is public.
        var workflow = Workflow;

        Assert.DoesNotContain("api-key:", workflow, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("environment:", workflow, StringComparison.Ordinal);
    }
}
