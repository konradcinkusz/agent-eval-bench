using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using AbsenceConcierge.Evals.Scenarios;

namespace AbsenceConcierge.Evals.Reporting;

/// <summary>
/// The recorded pass state a regression is measured against.
///
/// <para>
/// Constraint scenarios do not need it — they must pass at 100% on every run, and a
/// baseline that recorded one as failing would be a merged violation with a
/// signature next to it. It exists for the behaviour scenarios, which are measured
/// rather than gated, and for the honesty of knowing what "green" meant last time.
/// </para>
/// <para>
/// It carries the spec version it was recorded against, and the harness refuses to
/// compare across versions. That is SPEC §8.4 made mechanical: a fixture edit or a
/// contract change moves what the number measured, and a baseline compared across
/// that boundary is a measuring stick that changed length between readings.
/// </para>
/// </summary>
public sealed record Baseline(
    int Layer,
    string SpecVersion,
    string Interpreter,
    string Recorded,
    Dictionary<string, string> Scenarios,
    string? CorpusDigest = null)
{
    private static readonly JsonSerializerOptions Format = new()
    {
        PropertyNameCaseInsensitive = true,
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true,
    };

    [JsonIgnore]
    public static string Path => System.IO.Path.Combine(RepositoryLayout.BaselinesDirectory, "layer1.json");

    public static Baseline Load()
    {
        if (!File.Exists(Path))
        {
            throw new FileNotFoundException(
                $"No Layer 1 baseline at '{Path}'. A suite with no recorded state cannot tell a "
                + "regression from a Tuesday.",
                Path);
        }

        return JsonSerializer.Deserialize<Baseline>(File.ReadAllText(Path), Format)
            ?? throw new InvalidOperationException($"Baseline '{Path}' is empty.");
    }

    public string StatusOf(string scenarioId) =>
        Scenarios.TryGetValue(scenarioId, out var status) ? status : "unrecorded";

    /// <summary>
    /// A digest of everything that decides what a run measures.
    ///
    /// <para>
    /// The version string above is a promise: it says a human believed something
    /// changed. It cannot say what, and it does not fire when a version moves for
    /// an unrelated reason while a scenario edit rides along. Worse,
    /// <c>evals/scenarios/</c> is in neither of check-change-coupling's rules, so
    /// an <c>expect</c> block can be weakened with no bump at all and the harness
    /// will compare the new measurement against the old number and call it no
    /// regression. This turns that guard from "somebody remembered" into "the
    /// bytes agree".
    /// </para>
    /// <para>
    /// It hashes the semantics rather than the files. Digesting raw YAML would
    /// invalidate the baseline every time somebody reworded a <c>why</c>, which
    /// trains people to re-record without reading — the opposite of what this is
    /// for. So: the fields that change what is measured, and the fixture worlds
    /// the scenarios' <c>overrides</c> merge onto. Not <c>title</c>, <c>why</c>,
    /// <c>origin</c>, or <c>rubrics</c>, which is Layer 2's concern.
    /// </para>
    /// </summary>
    public static string DigestOf(IEnumerable<LoadedScenario> corpus)
    {
        ArgumentNullException.ThrowIfNull(corpus);

        var canonical = new StringBuilder();

        // Ordered by id, so the digest does not depend on the order the loader
        // happened to walk the directory in.
        foreach (var loaded in corpus.OrderBy(scenario => scenario.Id, StringComparer.Ordinal))
        {
            var scenario = loaded.Scenario;

            canonical.Append(JsonSerializer.Serialize(
                new
                {
                    scenario.Id,
                    scenario.Class,
                    scenario.Gate,
                    scenario.Fixture,
                    scenario.Conversation,
                    scenario.Expect,
                    scenario.Skip,
                },
                Format));
            canonical.Append('\n');
        }

        foreach (var path in Directory
            .EnumerateFiles(RepositoryLayout.FixturesDirectory, "*.yaml")
            .OrderBy(path => System.IO.Path.GetFileName(path), StringComparer.Ordinal))
        {
            canonical.Append(System.IO.Path.GetFileName(path)).Append('\n');
            canonical.Append(File.ReadAllText(path)).Append('\n');
        }

        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(canonical.ToString()));
        return $"sha256:{Convert.ToHexString(hash).ToLowerInvariant()[..16]}";
    }
}
