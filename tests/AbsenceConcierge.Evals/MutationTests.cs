using AbsenceConcierge.Evals.Mutations;
using AbsenceConcierge.Evals.Reporting;

namespace AbsenceConcierge.Evals;

/// <summary>
/// Proves the suite can fail.
///
/// <para>
/// Every other test in this project asks whether the agent is right. These ask
/// whether the <em>suite</em> would notice if it were not — which is a different
/// question, and the one an eval suite is least likely to have an answer to.
/// AI-EVALS.md has no mutation requirement; SPEC §8.6 adds one, and this is where
/// it is paid.
/// </para>
/// <para>
/// Deliberately not a per-commit gate. The estate frames mutation testing as a
/// suite-health signal rather than a merge gate, and these run with the rest of the
/// suite because the whole thing takes seconds — if that stops being true, this is
/// the file that moves to the nightly run, not the file that gets deleted.
/// </para>
/// </summary>
public sealed class MutationTests
{
    public static TheoryData<string> Variants =>
        new(BrokenAgents.All.Select(variant => variant.Name));

    [Theory]
    [MemberData(nameof(Variants))]
    public void A_broken_agent_is_caught_by_the_scenario_that_covers_it(string variantName)
    {
        var variant = BrokenAgents.All.Single(candidate =>
            string.Equals(candidate.Name, variantName, StringComparison.Ordinal));

        var scenario = Layer1Run.Corpus.Single(candidate =>
            string.Equals(candidate.Id, variant.ScenarioId, StringComparison.Ordinal));

        // Sanity first: the scenario must pass with the real agent, or "it failed
        // with the mutant" would prove nothing about the mutant.
        Assert.True(
            Layer1Run.Report[variant.ScenarioId].Passed,
            $"{variant.ScenarioId} does not pass with the real agent, so it cannot be used to prove "
            + $"that the variant '{variantName}' is caught.");

        var mutated = Layer1Run.RunOne(scenario, variant.Break);

        Assert.True(
            !mutated.Passed,
            $"The variant '{variantName}' survived {variant.ScenarioId}. That is a missing assertion, "
            + "not a curiosity: the suite cannot currently tell this broken agent from the real one.");

        // The condition here used to accept ScenarioStatus.Error as well, which
        // contradicted its own message: a mutant that crashes the harness was being
        // recorded as one the SUITE caught. Those are different results, and only
        // one of them is evidence about the assertions. A crash says the broken
        // agent was too broken to grade — a scenario that never ran cannot be said
        // to have noticed anything.
        Assert.NotEqual(ScenarioStatus.Error, mutated.Status);

        Assert.True(
            mutated.Failures.Count > 0,
            $"The variant '{variantName}' was marked failing with no failing assertion, which means the "
            + "harness, not the suite, caught it.");
    }

    [Fact]
    public void Every_variant_breaks_a_different_constraint()
    {
        // Four variants covering one constraint between them would be a mutation pass
        // that felt thorough and proved one thing four times.
        var scenarios = BrokenAgents.All.Select(variant => variant.ScenarioId).ToList();

        Assert.Equal(scenarios.Count, scenarios.Distinct(StringComparer.Ordinal).Count());
        // Seven: C-1 and C-5 through C-7 from the first pass, plus C-2, C-3 and C-4,
        // whose assertions had never been shown able to catch anything.
        Assert.Equal(7, BrokenAgents.All.Count);
    }
}
