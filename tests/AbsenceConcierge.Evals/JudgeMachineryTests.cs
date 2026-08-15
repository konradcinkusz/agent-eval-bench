using AbsenceConcierge.Evals.Execution;
using AbsenceConcierge.Evals.Judging;
using AbsenceConcierge.Evals.Reporting;

namespace AbsenceConcierge.Evals;

/// <summary>
/// The judge's machinery, exercised with no credential and no network.
///
/// <para>
/// This repository ships without a key, so the model has never answered here. That
/// is recorded honestly in <c>docs/DEVIATIONS.md</c> — but "the model has not been
/// called" is a much smaller gap than "none of this code has ever run", and these
/// tests are the difference. Prompt assembly, strict parsing, every rejection path,
/// the transcript the judge reads and the arithmetic behind calibration all execute
/// on every push, against replies written by hand to be exactly what a model would
/// send back.
/// </para>
/// </summary>
public sealed class JudgeMachineryTests
{
    private const string Good = """
        {
          "scores": [
            { "rubric": "grounding", "score": 3, "justification": "Every date in the reply matches the confirmation event." },
            { "rubric": "tone", "score": 2, "justification": "Two sentences, no enthusiasm about being unwell." }
          ]
        }
        """;

    [Fact]
    public void A_well_formed_verdict_parses()
    {
        var scores = RubricJudge.Parse(Good, ["grounding", "tone"]);

        Assert.Collection(
            scores,
            score => Assert.Equal(("grounding", 3), (score.Rubric, score.Score)),
            score => Assert.Equal(("tone", 2), (score.Rubric, score.Score)));
    }

    [Fact]
    public void A_verdict_wrapped_in_a_fenced_block_still_parses()
    {
        // Worth tolerating: it changes no score, and models do it even when told not
        // to. Prose INSTEAD of an object is a different matter and is rejected below.
        var fenced = "Here you go:\n```json\n" + Good + "\n```";

        Assert.Equal(2, RubricJudge.Parse(fenced, ["grounding", "tone"]).Count);
    }

    [Fact]
    public void Prose_instead_of_a_verdict_is_an_error()
    {
        var exception = Assert.Throws<InvalidOperationException>(
            () => RubricJudge.Parse("I think the assistant did quite well overall.", ["tone"]));

        Assert.Contains("readable JSON", exception.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void A_missing_criterion_is_not_a_zero_and_is_not_a_pass()
    {
        // The most dangerous of the rejections. Treating an omission as zero moves a
        // threshold on a number nobody produced; treating it as absent-and-fine grades
        // a criterion that was never measured.
        var exception = Assert.Throws<InvalidOperationException>(
            () => RubricJudge.Parse(Good, ["grounding", "tone", "confirmation-clarity"]));

        Assert.Contains("confirmation-clarity", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void A_score_off_the_scale_is_rejected()
    {
        const string OffScale = """
            { "scores": [ { "rubric": "tone", "score": 3, "justification": "Excellent." } ] }
            """;

        // `tone` is 0–2. A 3 has no anchor behind it, so it cannot be compared to a
        // human label and must not enter a mean.
        var exception = Assert.Throws<InvalidOperationException>(() => RubricJudge.Parse(OffScale, ["tone"]));

        Assert.Contains("0–2", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void A_score_with_no_justification_is_rejected()
    {
        const string Unjustified = """
            { "scores": [ { "rubric": "tone", "score": 2, "justification": "  " } ] }
            """;

        var exception = Assert.Throws<InvalidOperationException>(() => RubricJudge.Parse(Unjustified, ["tone"]));

        Assert.Contains("justification", exception.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Criteria_nobody_asked_for_are_rejected()
    {
        // A judge inventing criteria has stopped following the rubric file, and the
        // rubric file is half the pin.
        var exception = Assert.Throws<InvalidOperationException>(
            () => RubricJudge.Parse(Good, ["grounding"]));

        Assert.Contains("tone", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void A_decimal_score_is_rejected_rather_than_rounded()
    {
        const string Fractional = """
            { "scores": [ { "rubric": "tone", "score": 1.5, "justification": "Between the anchors." } ] }
            """;

        // Rounding it would invent a level the anchors do not describe, which is the
        // whole reason the scales are ordinal and small.
        Assert.Throws<InvalidOperationException>(() => RubricJudge.Parse(Fractional, ["tone"]));
    }

    [Fact]
    public void The_transcript_carries_the_trace_and_not_only_the_reply()
    {
        // SPEC §5: the judge sees the trace, or it grades fluency and calls it
        // grounding. This is that requirement, checked.
        var scenario = Layer1Run.Corpus.Single(candidate =>
            candidate.Id == "hap-001-sick-today-and-tomorrow");

        var transcript = TraceNarrative.Render(scenario, ScenarioRunner.Execute(scenario));

        Assert.Contains("tool `list_leave_types`", transcript, StringComparison.Ordinal);
        Assert.Contains("returned lt-201", transcript, StringComparison.Ordinal);
        Assert.Contains("event `confirmation.shown`", transcript, StringComparison.Ordinal);
        Assert.Contains("outcome `completed`", transcript, StringComparison.Ordinal);
        Assert.Contains("I'm sick today and probably tomorrow", transcript, StringComparison.Ordinal);
    }

    [Fact]
    public void The_same_run_renders_to_the_same_transcript()
    {
        // A transcript that varied between runs would make the score vary with it and
        // leave nobody able to say which had changed.
        var scenario = Layer1Run.Corpus.Single(candidate => candidate.Id == "den-005-payroll-question");

        var first = TraceNarrative.Render(scenario, ScenarioRunner.Execute(scenario));
        var second = TraceNarrative.Render(scenario, ScenarioRunner.Execute(scenario));

        Assert.Equal(first, second);
    }
}

/// <summary>
/// The calibration arithmetic, which decides whether Layer 2's scores may gate.
/// It runs with no credential because it is arithmetic over two lists of numbers.
/// </summary>
public sealed class CalibrationTests
{
    [Fact]
    public void Perfect_agreement_on_a_single_category_is_undefined_rather_than_perfect()
    {
        // The trap in κ. Two raters who both said "3" to everything agree completely
        // and have demonstrated nothing, because chance alone predicts it. Reporting
        // 1.0 there would let a judge be certified by a labeller who was not looking.
        Assert.Null(Calibration.CohenKappa([(3, 3), (3, 3), (3, 3)]));
    }

    [Fact]
    public void Complete_agreement_across_categories_is_one()
    {
        Assert.Equal(1.0, Calibration.CohenKappa([(3, 3), (2, 2), (1, 1), (0, 0)])!.Value, precision: 6);
    }

    [Fact]
    public void Complete_disagreement_is_negative()
    {
        var kappa = Calibration.CohenKappa([(3, 0), (0, 3), (3, 0), (0, 3)]);

        Assert.NotNull(kappa);
        Assert.True(kappa < 0, $"κ was {kappa}, which should be below zero for systematic disagreement.");
    }

    [Fact]
    public void One_pair_is_not_enough_to_compute_anything()
    {
        Assert.Null(Calibration.CohenKappa([(3, 3)]));
    }

    [Fact]
    public void With_no_labels_the_judge_may_not_gate()
    {
        // The honest current state, asserted rather than assumed. When labels start
        // arriving this test is what will notice that the position has changed.
        var report = Calibration.Overall(new Dictionary<string, IReadOnlyList<RubricScore>>(StringComparer.Ordinal));

        Assert.False(report.Gating);
        Assert.Contains("gate nothing", report.Reason, StringComparison.OrdinalIgnoreCase);
    }
}
