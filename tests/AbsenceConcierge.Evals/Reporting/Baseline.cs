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
    Dictionary<string, string> Scenarios)
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
}
