using AbsenceConcierge.Evals.Assertions;
using AbsenceConcierge.Evals.Execution;
using AbsenceConcierge.Evals.Extraction;
using AbsenceConcierge.Evals.Mutations;
using AbsenceConcierge.Evals.Scenarios;

namespace AbsenceConcierge.Evals;

/// <summary>
/// A trace becomes a scenario by extraction rather than by authorship.
///
/// <para>
/// AI-EVALS.md §3 requires a production incident to become a scenario before it
/// becomes a fix. Every repository that adopts that rule discovers the same thing:
/// the scenario gets written from what somebody remembers about the incident, and
/// the assertion that would have caught it is the one nobody thought to write. So
/// the path tested here does not go through a person's memory — the trace is read
/// mechanically, and the scenario it produces is run again.
/// </para>
/// <para>
/// The second test is the one that matters. A scenario derived from a passing run
/// could easily be a set of tautologies, and a suite of tautologies is worse than no
/// suite: it is green, it is large, and it is evidence of nothing. So the extracted
/// scenario is put in front of a deliberately broken agent, and has to fail.
/// </para>
/// </summary>
public sealed class ExtractionTests : IDisposable
{
    private const string SourceScenario = "hap-001-sick-today-and-tomorrow";

    private readonly List<string> _written = [];

    /// <summary>
    /// The extracted scenario has to exist on disk for the length of the test:
    /// <c>FixtureComposer</c> re-reads a scenario's file to find its <c>overrides</c>
    /// block, which is the price of merging fixtures at the YAML node level.
    /// </summary>
    public void Dispose()
    {
        foreach (var path in _written.Where(File.Exists))
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void A_trace_replays_as_a_scenario_that_passes_against_the_same_world()
    {
        var original = Corpus(SourceScenario);
        var extracted = Extract(original);

        var replay = ScenarioRunner.Execute(extracted);

        var outcomes = extracted.Scenario.Expect
            .Select(assertion => AssertionEvaluator.Evaluate(assertion, replay))
            .ToList();

        // Not just "nothing failed": an extractor that produced no assertions would
        // satisfy that trivially, and this whole test would be a green tautology.
        Assert.NotEmpty(outcomes);

        var failures = outcomes.Where(outcome => !outcome.Passed).ToList();

        Assert.True(
            failures.Count == 0,
            "An extracted scenario did not replay:\n"
            + string.Join('\n', failures.Select(failure => $"  {failure.Assertion} — {failure.Detail}")));
    }

    [Fact]
    public void An_extracted_scenario_catches_the_agent_the_source_scenario_catches()
    {
        // The teeth check. `fabricates-a-leave-type` is the variant BrokenAgents names
        // hap-001 as the catcher of; a scenario extracted from hap-001's own trace has
        // to catch it too, or extraction produced description rather than assertion.
        var extracted = Extract(Corpus(SourceScenario));
        var variant = BrokenAgents.All.Single(candidate =>
            string.Equals(candidate.ScenarioId, SourceScenario, StringComparison.Ordinal));

        var broken = ScenarioRunner.Execute(extracted, variant.Break);

        var failures = extracted.Scenario.Expect
            .Select(assertion => AssertionEvaluator.Evaluate(assertion, broken))
            .Where(outcome => !outcome.Passed)
            .ToList();

        Assert.True(
            failures.Count > 0,
            $"The '{variant.Name}' agent passed every assertion extracted from {SourceScenario}. "
            + "Extraction produced a description of a run, not a test of one.");
    }

    [Fact]
    public void Extraction_asserts_what_did_not_happen()
    {
        // The half a person reconstructing an incident forgets. hap-001 never looks
        // anybody up in the directory, and a scenario that does not say so would pass
        // for an agent that started doing it.
        var extracted = Extract(Corpus(SourceScenario));

        var absent = extracted.Scenario.Expect
            .Where(assertion => string.Equals(assertion.Assert, "tool_not_called", StringComparison.Ordinal))
            .Select(assertion => assertion.Tool)
            .ToList();

        Assert.Contains("find_employee", absent, StringComparer.Ordinal);
    }

    [Fact]
    public void An_extracted_scenario_cannot_be_committed_until_a_human_has_read_it()
    {
        // Extraction records what the agent did. Whether it should have is a
        // judgement, and the marker is what stops "this is what happened" being
        // merged as "this is what must happen" — scripts/validate-scenarios.mjs
        // rejects any scenario still carrying one.
        var extracted = Extract(Corpus(SourceScenario));

        Assert.StartsWith(ScenarioExtractor.ReviewMarker, extracted.Scenario.Why, StringComparison.Ordinal);
        Assert.StartsWith(ScenarioExtractor.ReviewMarker, extracted.Scenario.Title, StringComparison.Ordinal);
        Assert.Equal("production-trace", extracted.Scenario.Origin?.Kind);
        Assert.Equal("behaviour", extracted.Scenario.Gate);
    }

    [Fact]
    public void The_corpus_validator_rejects_the_marker_this_extractor_writes()
    {
        // The marker is a rule spread across two languages, and the failure mode is
        // silent: reword one side and extracted scenarios sail into the corpus with
        // nothing going red. So the C# constant is compared to the literal in the
        // script, the same way NightlyWorkflowTests reads the workflow it depends on.
        var script = Path.Combine(RepositoryLayout.Root, "scripts", "validate-scenarios.mjs");

        Assert.True(File.Exists(script), $"The corpus validator is not at '{script}'.");

        Assert.Contains(
            $"const REVIEW_MARKER = '{ScenarioExtractor.ReviewMarker}'",
            File.ReadAllText(script),
            StringComparison.Ordinal);
    }

    /// <summary>
    /// Runs a corpus scenario, extracts a new one from its trace, writes it as YAML
    /// and reads it back through the corpus loader.
    ///
    /// <para>
    /// The round trip through text is not ceremony. What gets committed is a file,
    /// and an extractor whose in-memory object is right while its YAML quotes
    /// <c>2026-08-11</c> as a date produces a scenario that fails for a reason no
    /// reviewer will find in the diff.
    /// </para>
    /// </summary>
    private LoadedScenario Extract(LoadedScenario original)
    {
        var run = ScenarioRunner.Execute(original);

        var scenario = ScenarioExtractor.From(
            run,
            new ExtractionRequest(
                $"{original.Id}-extracted",
                original.Scenario.Class,
                original.Scenario.Fixture,
                original.Scenario.Conversation,
                Reference: "round-trip test, no production trace involved",
                Date: "2026-08-15"));

        var yaml = ScenarioYaml.Write(scenario);

        // Written beside the source scenario so that FixtureComposer resolves the same
        // relative world. It carries no `overrides` block — the extractor cannot emit
        // one — so this only matters for scenarios whose delta is empty, which is why
        // the source here is one of them.
        var path = Path.Combine(
            Path.GetDirectoryName(original.Path)!,
            $"{scenario.Id}.yaml.tmp");

        File.WriteAllText(path, yaml);
        _written.Add(path);

        return ScenarioLoader.Parse(yaml, path);
    }

    private static LoadedScenario Corpus(string id) =>
        ScenarioLoader.LoadAll().Single(scenario =>
            string.Equals(scenario.Id, id, StringComparison.Ordinal));
}
