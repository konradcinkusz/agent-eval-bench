using System.Text.Json;
using System.Text.Json.Serialization;
using AbsenceConcierge.Evals.Assertions;

namespace AbsenceConcierge.Evals.Reporting;

/// <summary>
/// What a scenario did. Five values, and the two skips are <b>not</b> the same fact
/// (SPEC §8.5): one says a capability does not exist yet, the other says a run could
/// not happen for want of a credential. A harness that printed one number for both
/// would hide the trap the estate names — a config no CI context ever executes is
/// not a latent capability, it is documentation that lies.
/// </summary>
public static class ScenarioStatus
{
    public const string Pass = "pass";
    public const string Fail = "fail";

    /// <summary>The harness itself broke: an unreadable fixture, an assertion it does not understand.</summary>
    public const string Error = "error";

    public const string SkippedUnimplemented = "skipped:unimplemented";
    public const string SkippedNoCredential = "skipped:no-credential";
}

public sealed record ScenarioResult(
    string Id,
    string Class,
    string Gate,
    string Status,
    IReadOnlyList<AssertionOutcome> Assertions,
    string? SkipReason,
    string? Error,
    long DurationMs)
{
    [JsonIgnore]
    public bool IsConstraint => string.Equals(Gate, "constraint", StringComparison.Ordinal);

    [JsonIgnore]
    public bool Passed => string.Equals(Status, ScenarioStatus.Pass, StringComparison.Ordinal);

    [JsonIgnore]
    public bool Ran => Status is ScenarioStatus.Pass or ScenarioStatus.Fail or ScenarioStatus.Error;

    /// <summary>The failing assertions, in the order the scenario wrote them.</summary>
    [JsonIgnore]
    public IReadOnlyList<AssertionOutcome> Failures =>
        [.. Assertions.Where(assertion => !assertion.Passed)];
}

/// <summary>
/// One Layer 1 run, in a shape both a human and Phase 6's pull-request comment can
/// read.
///
/// <para>
/// It records which interpreter produced it, because a baseline gathered under one
/// does not describe the other and merging them silently would be measuring with a
/// ruler that changes length (ADR-0004).
/// </para>
/// </summary>
public sealed record EvalReport(
    int Layer,
    string SpecVersion,
    string Interpreter,
    long DurationMs,
    IReadOnlyList<ScenarioResult> Scenarios)
{
    private static readonly JsonSerializerOptions Format = new()
    {
        WriteIndented = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    [JsonIgnore]
    public int Total => Scenarios.Count;

    [JsonIgnore]
    public int Passed => Scenarios.Count(result => result.Passed);

    [JsonIgnore]
    public int Failed => Scenarios.Count(result =>
        result.Status is ScenarioStatus.Fail or ScenarioStatus.Error);

    [JsonIgnore]
    public int SkippedUnimplemented =>
        Scenarios.Count(result => result.Status == ScenarioStatus.SkippedUnimplemented);

    [JsonIgnore]
    public int SkippedNoCredential =>
        Scenarios.Count(result => result.Status == ScenarioStatus.SkippedNoCredential);

    public ScenarioResult this[string id] =>
        Scenarios.SingleOrDefault(result => string.Equals(result.Id, id, StringComparison.Ordinal))
        ?? throw new KeyNotFoundException($"No result for scenario '{id}'.");

    public void WriteTo(string path)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, JsonSerializer.Serialize(this, Format));
    }

    /// <summary>
    /// The run, in the form that goes to a console and a CI step summary. Skips are
    /// printed on their own line and by kind, so a run that skipped everything can
    /// never read as a run that passed everything.
    /// </summary>
    public string Summarise()
    {
        var lines = new List<string>
        {
            $"Layer 1 — spec {SpecVersion}, interpreter '{Interpreter}', {DurationMs} ms",
            $"  {Passed}/{Total} passed · {Failed} failed",
            $"  skipped:unimplemented {SkippedUnimplemented} · skipped:no-credential {SkippedNoCredential}",
        };

        foreach (var group in Scenarios.GroupBy(result => result.Class).OrderBy(g => g.Key, StringComparer.Ordinal))
        {
            lines.Add($"  {group.Key,-12} {group.Count(r => r.Passed)}/{group.Count()}");
        }

        foreach (var failure in Scenarios.Where(result => !result.Passed && result.Ran))
        {
            lines.Add($"  ✗ {failure.Id} [{failure.Gate}]");

            foreach (var assertion in failure.Failures)
            {
                lines.Add($"      {assertion.Assertion} — {assertion.Detail}");
            }

            if (failure.Error is { } error)
            {
                lines.Add($"      harness error: {error}");
            }
        }

        return string.Join(Environment.NewLine, lines);
    }
}
