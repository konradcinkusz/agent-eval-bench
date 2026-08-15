using AbsenceConcierge.AgentService.Workforce.Fixtures;
using AbsenceConcierge.Evals.Scenarios;
using YamlDotNet.RepresentationModel;
using YamlDotNet.Serialization;
using YamlDotNet.Serialization.NamingConventions;

namespace AbsenceConcierge.Evals.World;

/// <summary>
/// Builds a scenario's world: the named base fixture, with the scenario's sparse
/// delta applied on top.
///
/// <para>
/// <b>The merge happens at the YAML node level, not through .NET types.</b> Loading
/// the base into a dictionary and writing it back out would re-decide every scalar's
/// style — a quoted <c>'2026-08-20'</c> can come back as a date, <c>true</c> as a
/// string — and the resulting world would differ from the one the service loads for
/// reasons no reviewer could see in the diff. Copying nodes preserves the file.
/// </para>
/// <para>
/// <b>A key present in the delta replaces the base's key wholesale.</b> Not a deep
/// merge: <c>adv-003</c> overrides <c>leave_types</c> with a two-element list where
/// the base has four, and a deep merge would have quietly given it six. The
/// scenarios are written against replacement, and replacement is what a reader of
/// one scenario file can predict without opening the base.
/// </para>
/// <para>
/// Every scenario reconstructs its world from scratch and nothing survives between
/// them (SPEC §8.3) — surviving state is the named cause of nondeterministic evals.
/// </para>
/// </summary>
public static class FixtureComposer
{
    private static readonly IDeserializer Deserializer = new DeserializerBuilder()
        .WithNamingConvention(UnderscoredNamingConvention.Instance)
        .IgnoreUnmatchedProperties()
        .Build();

    public static WorkforceWorld Compose(LoadedScenario loaded)
    {
        ArgumentNullException.ThrowIfNull(loaded);

        var basePath = Path.Combine(
            RepositoryLayout.FixturesDirectory,
            $"{loaded.Scenario.Fixture.Base}.yaml");

        if (!File.Exists(basePath))
        {
            throw new FileNotFoundException(
                $"Scenario '{loaded.Id}' names base fixture '{loaded.Scenario.Fixture.Base}', which does "
                + $"not exist at '{basePath}'.");
        }

        var world = RootOf(File.ReadAllText(basePath), basePath);
        var overrides = OverridesOf(loaded.Path);

        if (overrides is not null)
        {
            foreach (var (key, value) in overrides.Children)
            {
                world.Children[key] = value;
            }
        }

        var merged = Render(world);
        var file = Deserializer.Deserialize<FixtureFile>(merged)
            ?? throw new InvalidOperationException($"Scenario '{loaded.Id}' composed an empty fixture.");

        return WorkforceWorld.FromFile(file, $"{basePath} + {loaded.Id}");
    }

    /// <summary>
    /// The permission strings literally present in a scenario's fixture, from the
    /// two places they appear.
    ///
    /// <para>
    /// SPEC §2.4 is precise about this and the precision is the point: the harness
    /// <em>enumerates</em> the values rather than pattern-matching for them, because
    /// a regex like <c>^[a-z]+:[a-z]+$</c> flags ordinary prose, and a rule that
    /// fires on prose is a rule that gets switched off. It reads the tool policy as
    /// well as the actor's grants, because <c>den-004</c> removes a permission from
    /// the actor and then requires the refusal not to name it — the one case reading
    /// only the actor's list would miss.
    /// </para>
    /// </summary>
    public static IReadOnlyCollection<string> PermissionVocabulary(LoadedScenario loaded)
    {
        ArgumentNullException.ThrowIfNull(loaded);

        var basePath = Path.Combine(
            RepositoryLayout.FixturesDirectory,
            $"{loaded.Scenario.Fixture.Base}.yaml");

        var world = RootOf(File.ReadAllText(basePath), basePath);
        var overrides = OverridesOf(loaded.Path);

        if (overrides is not null)
        {
            foreach (var (key, value) in overrides.Children)
            {
                world.Children[key] = value;
            }
        }

        var found = new HashSet<string>(StringComparer.Ordinal);

        if (world.Children.TryGetValue(new YamlScalarNode("actor"), out var actor)
            && actor is YamlMappingNode actorMap
            && actorMap.Children.TryGetValue(new YamlScalarNode("permissions"), out var granted)
            && granted is YamlSequenceNode grants)
        {
            foreach (var grant in grants.Children.OfType<YamlScalarNode>())
            {
                Add(found, grant.Value);
            }
        }

        if (world.Children.TryGetValue(new YamlScalarNode("tool_policy"), out var policy)
            && policy is YamlMappingNode policies)
        {
            foreach (var entry in policies.Children.Values.OfType<YamlMappingNode>())
            {
                if (entry.Children.TryGetValue(new YamlScalarNode("requires_permission"), out var required)
                    && required is YamlScalarNode scalar)
                {
                    Add(found, scalar.Value);
                }
            }
        }

        return found;
    }

    private static void Add(HashSet<string> into, string? value)
    {
        if (!string.IsNullOrWhiteSpace(value))
        {
            into.Add(value);
        }
    }

    private static YamlMappingNode RootOf(string yaml, string source)
    {
        var stream = new YamlStream();
        stream.Load(new StringReader(yaml));

        if (stream.Documents.Count == 0 || stream.Documents[0].RootNode is not YamlMappingNode root)
        {
            throw new InvalidOperationException($"'{source}' is not a YAML mapping.");
        }

        return root;
    }

    private static YamlMappingNode? OverridesOf(string scenarioPath)
    {
        var root = RootOf(File.ReadAllText(scenarioPath), scenarioPath);

        if (!root.Children.TryGetValue(new YamlScalarNode("fixture"), out var fixture)
            || fixture is not YamlMappingNode fixtureMap)
        {
            return null;
        }

        return fixtureMap.Children.TryGetValue(new YamlScalarNode("overrides"), out var overrides)
            ? overrides as YamlMappingNode
            : null;
    }

    private static string Render(YamlMappingNode root)
    {
        var stream = new YamlStream(new YamlDocument(root));
        using var writer = new StringWriter();
        stream.Save(writer, assignAnchors: false);
        return writer.ToString();
    }
}
