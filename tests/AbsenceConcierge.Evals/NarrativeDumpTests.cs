using AbsenceConcierge.Evals.Execution;
using AbsenceConcierge.Evals.Judging;
using AbsenceConcierge.Evals.Reporting;
using AbsenceConcierge.Evals.Scenarios;

namespace AbsenceConcierge.Evals;

/// <summary>
/// The labelling utility: dumps, on request, exactly the transcript the judge
/// would read for every judged scenario — and nothing the judge would say.
///
/// <para>
/// docs/CALIBRATION.md's one unbreakable rule is that a labeller must not read
/// the judge's scores first: reading them produces agreement with the judge
/// rather than a measurement of it, and the bias runs upward. This utility is
/// how that rule stays keepable in practice. It renders each scenario's
/// <see cref="TraceNarrative"/> — the same bytes
/// <see cref="Layer2Run"/> hands the judge, deterministic by construction — into
/// <c>TestResults/narratives/</c>, so a labeller can score transcripts against
/// the anchors in <c>evals/rubrics/judge.yaml</c> with no judge output anywhere
/// in sight.
/// </para>
/// <para>
/// Skipped unless <c>EVAL_DUMP_NARRATIVES=1</c>: it is a tool a labeller runs on
/// purpose, not a by-product every CI run pays for.
/// </para>
/// </summary>
public sealed class NarrativeDumpTests
{
    [Fact]
    public void The_judges_reading_material_is_dumped_for_labelling_when_asked()
    {
        if (Environment.GetEnvironmentVariable("EVAL_DUMP_NARRATIVES") != "1")
        {
            Assert.Skip("Set EVAL_DUMP_NARRATIVES=1 to dump the judged scenarios' transcripts for labelling.");
            return;
        }

        var directory = Path.Combine(RepositoryLayout.Root, "TestResults", "narratives");
        Directory.CreateDirectory(directory);

        var judged = Layer1Run.Corpus
            .Where(scenario => scenario.Scenario.Rubrics.Count > 0)
            .ToList();

        Assert.NotEmpty(judged);

        foreach (var scenario in judged)
        {
            var run = ScenarioRunner.Execute(scenario);

            var content =
                $"# {scenario.Id}\n\n"
                + $"Rubrics to label: {string.Join(", ", scenario.Scenario.Rubrics)}\n\n"
                + TraceNarrative.Render(scenario, run);

            File.WriteAllText(Path.Combine(directory, $"{scenario.Id}.md"), content);
        }

        Console.WriteLine($"Wrote {judged.Count} transcripts to {directory}.");
    }
}
