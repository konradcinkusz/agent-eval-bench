using AbsenceConcierge.AgentService.Workforce;
using AbsenceConcierge.AgentService.Workforce.Fixtures;
using AbsenceConcierge.Evals.Scenarios;
using YamlDotNet.Serialization;
using YamlDotNet.Serialization.NamingConventions;

namespace AbsenceConcierge.Evals;

/// <summary>
/// The fixture's claims about the boundary, checked against the boundary.
///
/// <para>
/// <c>tool_policy</c> sat in the base fixture under a comment saying it was "what
/// the mock enforces at the boundary regardless of what the agent asks for", and
/// adv-007's <c>why</c> repeated the claim. Both loaders build their deserializer
/// with <c>.IgnoreUnmatchedProperties()</c> and <see cref="FixtureFile"/> had no
/// matching property, so the block reached nothing: data everybody believed was
/// load-bearing, which is the documentation-that-lies failure this repository
/// exists to demonstrate against, one directory from where its own checks look.
/// </para>
/// <para>
/// The block is now loaded, and this is what makes loading it worth anything. The
/// fixture is <em>not</em> the source of the enforcement — it cannot be, because
/// the same boundary must hold for the MCP adapter, which serves no fixture — so
/// it is treated as a claim about <see cref="WorkforceToolCatalog"/> and checked
/// against it. Drift between the two is now a failing build rather than a
/// paragraph nobody re-read.
/// </para>
/// </summary>
public sealed class FixtureContractTests
{
    private static readonly IDeserializer Deserializer = new DeserializerBuilder()
        .WithNamingConvention(UnderscoredNamingConvention.Instance)
        .IgnoreUnmatchedProperties()
        .Build();

    private static List<(string Name, FixtureFile File)> Fixtures() =>
        [.. Directory
            .EnumerateFiles(RepositoryLayout.FixturesDirectory, "*.yaml")
            .OrderBy(path => path, StringComparer.Ordinal)
            .Select(path => (
                Path.GetFileNameWithoutExtension(path),
                Deserializer.Deserialize<FixtureFile>(File.ReadAllText(path))
                    ?? throw new InvalidOperationException($"Fixture '{path}' is empty.")))];

    [Fact]
    public void The_fixtures_are_readable_and_there_are_some()
    {
        // A test that enumerates nothing passes for the wrong reason.
        var fixtures = Fixtures();

        Assert.NotEmpty(fixtures);
        Assert.All(fixtures, entry => Assert.NotEmpty(entry.File.LeaveTypes));
    }

    [Fact]
    public void Every_declared_tool_policy_names_a_tool_that_exists()
    {
        foreach (var (name, file) in Fixtures())
        {
            foreach (var tool in file.ToolPolicy.Keys)
            {
                Assert.True(
                    WorkforceToolCatalog.Names.Contains(tool),
                    $"Fixture '{name}' declares a policy for '{tool}', which is not a tool in the "
                    + "catalogue. A policy for a tool nobody calls is a rule that matches nothing.");
            }
        }
    }

    [Fact]
    public void Every_declared_permission_matches_the_catalogue()
    {
        // The fixture restates what the catalogue enforces. Restating is fine;
        // restating something DIFFERENT is how a reader ends up believing a
        // permission boundary the code does not have.
        foreach (var (name, file) in Fixtures())
        {
            foreach (var (tool, policy) in file.ToolPolicy)
            {
                if (policy.RequiresPermission is not { } declared)
                {
                    continue;
                }

                var actual = WorkforceToolCatalog.RequiredPermission(tool);

                Assert.True(
                    string.Equals(declared, actual, StringComparison.Ordinal),
                    $"Fixture '{name}' says '{tool}' requires '{declared}'; WorkforceToolCatalog "
                    + $"requires '{actual ?? "(none)"}'. The catalogue is the authority — the fixture "
                    + "is a claim about it, and this is the test that keeps the claim true.");
            }
        }
    }

    [Fact]
    public void The_write_tool_is_declared_self_only_and_past_rejecting()
    {
        // The two properties adv-007 and the past-date boundary test actually
        // depend on. If the fixture ever stops declaring them, the scenario `why`
        // that cites them has gone stale and should be caught here rather than
        // read as still true.
        var meridian = Fixtures().Single(entry => entry.Name == "meridian-labs").File;

        Assert.True(
            meridian.ToolPolicy.TryGetValue(WorkforceToolCatalog.RequestTimeOff, out var write),
            "The base fixture no longer declares a policy for request_time_off.");

        Assert.True(write!.OnlyForSelf, "request_time_off must be declared only_for_self.");
        Assert.True(write.RejectsPastDates, "request_time_off must be declared rejects_past_dates.");
    }
}
