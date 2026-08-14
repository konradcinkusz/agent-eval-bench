using YamlDotNet.Serialization;
using YamlDotNet.Serialization.NamingConventions;

namespace AbsenceConcierge.AgentService.Workforce.Fixtures;

public interface IFixtureLoader
{
    WorkforceWorld Load(string fixtureName);
}

/// <summary>
/// Loads a fixture by name from the <c>fixtures/</c> directory beside the binary,
/// which the build links from <c>evals/fixtures/</c>.
///
/// Two rules the estate's agent provisioner learned and this inherits: fail loudly
/// when the directory or the file is missing rather than reporting success over an
/// empty set, and verify the loaded set is non-empty before reporting success. A
/// mock that silently serves an empty world would make every scenario fail for the
/// same uninformative reason.
/// </summary>
public sealed class FixtureLoader(ILogger<FixtureLoader> logger, string fixtureDirectory) : IFixtureLoader
{
    private static readonly IDeserializer Deserializer = new DeserializerBuilder()
        .WithNamingConvention(UnderscoredNamingConvention.Instance)
        .IgnoreUnmatchedProperties()
        .Build();

    public WorkforceWorld Load(string fixtureName)
    {
        if (!Directory.Exists(fixtureDirectory))
        {
            throw new DirectoryNotFoundException(
                $"Fixture directory '{fixtureDirectory}' does not exist. It is linked from "
                + "evals/fixtures/ at build time; a missing directory means the link was dropped "
                + "from the csproj or the file was excluded from the container image.");
        }

        var path = Path.Combine(fixtureDirectory, $"{fixtureName}.yaml");

        if (!File.Exists(path))
        {
            var available = Directory.GetFiles(fixtureDirectory, "*.yaml")
                .Select(Path.GetFileNameWithoutExtension)
                .ToList();

            throw new FileNotFoundException(
                $"Fixture '{fixtureName}' not found at '{path}'. Available: "
                + (available.Count > 0 ? string.Join(", ", available) : "(none)"));
        }

        var file = Deserializer.Deserialize<FixtureFile>(File.ReadAllText(path))
            ?? throw new InvalidOperationException($"Fixture '{path}' is empty.");

        var world = WorkforceWorld.FromFile(file, path);

        if (world.LeaveTypes.Count == 0)
        {
            throw new InvalidOperationException(
                $"Fixture '{path}' declares no leave types. A world in which nothing can be "
                + "requested is almost certainly a mis-parsed file rather than a deliberate one.");
        }

        logger.LogInformation(
            "Loaded workforce fixture {Fixture}: {LeaveTypes} leave types, {Employees} colleagues, {Leaves} existing bookings",
            world.Name,
            world.LeaveTypes.Count,
            world.Employees.Count,
            world.ExistingLeaves.Count);

        return world;
    }
}
