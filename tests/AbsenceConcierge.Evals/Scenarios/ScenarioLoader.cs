using YamlDotNet.Serialization;
using YamlDotNet.Serialization.NamingConventions;

namespace AbsenceConcierge.Evals.Scenarios;

/// <summary>A scenario, with the path it came from.</summary>
public sealed record LoadedScenario(ScenarioFile Scenario, string Path)
{
    public string Id => Scenario.Id;

    public bool IsConstraint =>
        string.Equals(Scenario.Gate, "constraint", StringComparison.Ordinal);
}

/// <summary>
/// Reads every scenario in <c>evals/scenarios/</c>.
///
/// <para>
/// It refuses an empty corpus. The estate's provisioner learned this once already:
/// a loader that reports success over an empty set turns "nothing ran" into
/// "everything passed", and a suite that grades nothing is the most confident thing
/// in the repository.
/// </para>
/// </summary>
public static class ScenarioLoader
{
    private static readonly IDeserializer Deserializer = new DeserializerBuilder()
        .WithNamingConvention(UnderscoredNamingConvention.Instance)
        .IgnoreUnmatchedProperties()
        .Build();

    public static IReadOnlyList<LoadedScenario> LoadAll()
    {
        var directory = RepositoryLayout.ScenariosDirectory;

        if (!Directory.Exists(directory))
        {
            throw new DirectoryNotFoundException($"Scenario directory '{directory}' does not exist.");
        }

        var loaded = Directory
            .EnumerateFiles(directory, "*.yaml", SearchOption.AllDirectories)
            .OrderBy(path => path, StringComparer.Ordinal)
            .Select(Load)
            .ToList();

        if (loaded.Count == 0)
        {
            throw new InvalidOperationException(
                $"No scenarios found under '{directory}'. An empty corpus passes every gate, which "
                + "is why this is an error rather than a run with nothing in it.");
        }

        return loaded;
    }

    private static LoadedScenario Load(string path)
    {
        var scenario = Deserializer.Deserialize<ScenarioFile>(File.ReadAllText(path))
            ?? throw new InvalidOperationException($"Scenario '{path}' is empty.");

        if (string.IsNullOrWhiteSpace(scenario.Id))
        {
            throw new InvalidOperationException($"Scenario '{path}' has no id.");
        }

        return new LoadedScenario(scenario, path);
    }
}
