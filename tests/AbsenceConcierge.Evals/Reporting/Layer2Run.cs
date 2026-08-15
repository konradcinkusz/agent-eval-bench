using System.Diagnostics;
using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;
using AbsenceConcierge.AgentService.Agent.Llm;
using AbsenceConcierge.Evals.Execution;
using AbsenceConcierge.Evals.Judging;
using AbsenceConcierge.Evals.Scenarios;
using Microsoft.Extensions.Configuration;

namespace AbsenceConcierge.Evals.Reporting;

public sealed record Layer2ScenarioResult(
    string Id,
    string Class,
    string Status,
    IReadOnlyList<RubricScore> Scores,
    string? SkipReason,
    string? Error,
    int InputTokens,
    int OutputTokens,
    long DurationMs);

/// <param name="Rubric">Criterion name.</param>
/// <param name="Count">How many scenarios contributed a score.</param>
/// <param name="Mean">The mean across them.</param>
/// <param name="Lowest">The lowest single score, which is what a floor is checked against.</param>
/// <param name="Threshold">From evals/rubrics/judge.yaml.</param>
/// <param name="Floor">Null when the criterion has none.</param>
/// <param name="MeetsThreshold">Whether the mean reached the threshold.</param>
/// <param name="MeetsFloor">Whether every score cleared the floor. True when there is no floor.</param>
public sealed record RubricSummary(
    string Rubric,
    int Count,
    double Mean,
    int Lowest,
    double Threshold,
    int? Floor,
    bool MeetsThreshold,
    bool MeetsFloor);

public sealed record Layer2Report(
    int Layer,
    string Scope,
    string SpecVersion,
    string JudgeVersion,
    string RubricsHash,
    string PromptHash,
    string? Model,
    long DurationMs,
    int InputTokens,
    int OutputTokens,
    decimal? EstimatedCostUsd,
    CalibrationReport Calibration,
    IReadOnlyList<Layer2ScenarioResult> Scenarios,
    IReadOnlyList<RubricSummary> Rubrics)
{
    private static readonly JsonSerializerOptions Format = new()
    {
        WriteIndented = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
    };

    [JsonIgnore]
    public bool Ran => Scenarios.Any(scenario =>
        scenario.Status is ScenarioStatus.Pass or ScenarioStatus.Fail or ScenarioStatus.Error);

    [JsonIgnore]
    public int SkippedNoCredential =>
        Scenarios.Count(scenario => scenario.Status == ScenarioStatus.SkippedNoCredential);

    public void WriteTo(string path)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, JsonSerializer.Serialize(this, Format));
    }

    public string Summarise()
    {
        var lines = new List<string>
        {
            $"Layer 2 ({Scope}) — judge {JudgeVersion}, rubrics {RubricsHash}, prompt {PromptHash}",
            $"  model: {Model ?? "(none — no credential)"}, {DurationMs} ms, "
                + string.Create(CultureInfo.InvariantCulture, $"{InputTokens}+{OutputTokens} tokens")
                + (EstimatedCostUsd is { } cost
                    ? string.Create(CultureInfo.InvariantCulture, $", about ${cost:F4}")
                    : ", cost unknown (no prices configured)"),
            $"  calibration: {Calibration.Reason}",
        };

        if (!Ran)
        {
            lines.Add($"  skipped:no-credential {SkippedNoCredential} — nothing was graded on this run.");
            return string.Join(Environment.NewLine, lines);
        }

        foreach (var rubric in Rubrics)
        {
            var floor = rubric.Floor is { } value
                ? string.Create(CultureInfo.InvariantCulture, $", lowest {rubric.Lowest} (floor {value})")
                : string.Empty;

            lines.Add(string.Create(
                CultureInfo.InvariantCulture,
                $"  {rubric.Rubric,-22} mean {rubric.Mean:F2} / {rubric.Threshold:F2} over {rubric.Count}{floor}"
                + $" {(rubric.MeetsThreshold && rubric.MeetsFloor ? "ok" : "BELOW")}"));
        }

        foreach (var scenario in Scenarios.Where(scenario => scenario.Error is not null))
        {
            lines.Add($"  ! {scenario.Id}: {scenario.Error}");
        }

        return string.Join(Environment.NewLine, lines);
    }
}

/// <summary>
/// Runs Layer 2, or explains precisely why it did not.
///
/// <para>
/// <b>Without a credential every scenario is <c>skipped:no-credential</c>, and that
/// is legitimate on a pull request and nowhere else.</b> SPEC §8.5 names the trap: a
/// judge job that skips on every pull request <em>and</em> every nightly is the
/// config no CI context ever executes — documentation that lies. The keyed nightly
/// run is what keeps this layer honest, and a test in this project asserts that the
/// nightly workflow exists and passes the key, so "we will wire it up later" cannot
/// quietly become the permanent state.
/// </para>
/// </summary>
public static class Layer2Run
{
    private static readonly Lazy<Layer2Report> Lazy = new(Execute, LazyThreadSafetyMode.ExecutionAndPublication);

    public static Layer2Report Report => Lazy.Value;

    public static string ReportPath =>
        Path.Combine(RepositoryLayout.Root, "TestResults", "eval-report-layer2.json");

    /// <summary><c>smoke</c> on a pull request, <c>full</c> nightly.</summary>
    public static string Scope =>
        Environment.GetEnvironmentVariable("EVAL_LAYER2_SCOPE") is { Length: > 0 } scope
            ? scope.ToLowerInvariant()
            : "smoke";

    /// <summary>
    /// The judge's configuration, bound from the environment the same way the
    /// service binds its own — so a credential that works for the service works
    /// here, and neither reads a variable directly.
    /// </summary>
    public static LlmOptions? JudgeOptions()
    {
        var configuration = new ConfigurationBuilder().AddEnvironmentVariables().Build();

        var options = configuration.GetSection(LlmOptions.SectionName).Get<LlmOptions>() ?? new LlmOptions();

        // Deliberately ForJudge(): a judge that silently borrowed the agent's model
        // would make a changed score unattributable, because both sides of the
        // comparison would have moved at once (ADR-0004).
        var judge = options.ForJudge();

        return judge is { IsConfigured: true } ? judge : null;
    }

    private static Layer2Report Execute()
    {
        var configuration = JudgeConfiguration.Current;
        var stopwatch = Stopwatch.StartNew();

        var selected = Select(configuration);
        var options = JudgeOptions();

        var results = new List<Layer2ScenarioResult>();
        var judged = new Dictionary<string, IReadOnlyList<RubricScore>>(StringComparer.Ordinal);
        string? model = null;

        if (options is null)
        {
            foreach (var scenario in selected)
            {
                results.Add(new Layer2ScenarioResult(
                    scenario.Id,
                    scenario.Scenario.Class,
                    ScenarioStatus.SkippedNoCredential,
                    [],
                    "No judge model is configured (Llm:Provider, Llm:JudgeModel, Llm:Endpoint, Llm:ApiKey).",
                    null,
                    0,
                    0,
                    0));
            }
        }
        else
        {
            using var client = new HttpClient { Timeout = TimeSpan.FromSeconds(60) };
            var provider = LlmProviderFactory.Create(options, client)!;
            var judge = new RubricJudge(provider, configuration);

            foreach (var scenario in selected)
            {
                var result = Score(scenario, judge, configuration);
                results.Add(result);

                if (result.Scores.Count > 0)
                {
                    judged[scenario.Id] = result.Scores;
                    model ??= provider.ConfiguredModel;
                }
            }

            model = results.Count > 0 ? model : null;
        }

        stopwatch.Stop();

        var report = new Layer2Report(
            Layer: 2,
            Scope,
            SpecVersion: SpecVersionOf(),
            configuration.Version,
            configuration.RubricsHash,
            configuration.PromptHash,
            model,
            stopwatch.ElapsedMilliseconds,
            results.Sum(result => result.InputTokens),
            results.Sum(result => result.OutputTokens),
            EstimateCost(options, results),
            Calibration.Overall(judged),
            results,
            Summarise(results, configuration));

        report.WriteTo(ReportPath);
        Console.WriteLine(report.Summarise());

        return report;
    }

    /// <summary>
    /// The scenarios to grade: the explicit smoke list on a pull request, every
    /// scenario that names a rubric nightly.
    /// </summary>
    private static IReadOnlyList<LoadedScenario> Select(JudgeConfiguration configuration)
    {
        var withRubrics = Layer1Run.Corpus
            .Where(scenario => scenario.Scenario.Rubrics.Count > 0)
            .ToList();

        if (string.Equals(Scope, "full", StringComparison.Ordinal))
        {
            return withRubrics;
        }

        var wanted = configuration.Smoke.Select(entry => entry.Id).ToHashSet(StringComparer.Ordinal);

        return [.. withRubrics.Where(scenario => wanted.Contains(scenario.Id))];
    }

    private static Layer2ScenarioResult Score(
        LoadedScenario scenario,
        IRubricJudge judge,
        JudgeConfiguration configuration)
    {
        var stopwatch = Stopwatch.StartNew();

        try
        {
            var run = ScenarioRunner.Execute(scenario);
            var rubrics = scenario.Scenario.Rubrics;
            var prompt = configuration.BuildPrompt(rubrics, TraceNarrative.Render(scenario, run));

            var verdict = judge.ScoreAsync(prompt, rubrics).AsTask().GetAwaiter().GetResult();
            stopwatch.Stop();

            return new Layer2ScenarioResult(
                scenario.Id,
                scenario.Scenario.Class,
                ScenarioStatus.Pass,
                verdict.Scores,
                null,
                null,
                verdict.InputTokens,
                verdict.OutputTokens,
                stopwatch.ElapsedMilliseconds);
        }
#pragma warning disable CA1031 // One unreadable verdict must not take the report with it.
        catch (Exception exception)
        {
            stopwatch.Stop();

            // A judge that could not be read is not an agent that scored badly, and
            // reporting it as a low score would poison the mean with a number nobody
            // produced.
            return new Layer2ScenarioResult(
                scenario.Id,
                scenario.Scenario.Class,
                ScenarioStatus.Error,
                [],
                null,
                $"{exception.GetType().Name}: {exception.Message}",
                0,
                0,
                stopwatch.ElapsedMilliseconds);
        }
#pragma warning restore CA1031
    }

    private static IReadOnlyList<RubricSummary> Summarise(
        IReadOnlyList<Layer2ScenarioResult> results,
        JudgeConfiguration configuration)
    {
        var scores = results.SelectMany(result => result.Scores).ToList();

        return
        [
            .. scores
                .GroupBy(score => score.Rubric, StringComparer.Ordinal)
                .OrderBy(group => group.Key, StringComparer.Ordinal)
                .Select(group =>
                {
                    var rubric = configuration[group.Key];
                    var mean = group.Average(score => score.Score);
                    var lowest = group.Min(score => score.Score);

                    return new RubricSummary(
                        group.Key,
                        group.Count(),
                        mean,
                        lowest,
                        rubric.Threshold,
                        rubric.Floor,
                        mean >= rubric.Threshold,
                        rubric.Floor is not { } floor || lowest >= floor);
                }),
        ];
    }

    /// <summary>
    /// Money, when the rates are configured, and silence when they are not.
    ///
    /// Rates are not hardcoded here: they change, they differ per deployment, and a
    /// stale number in a public repository would be quoted back at somebody. Tokens
    /// are always reported because they are the fact; dollars are an estimate that
    /// needs an input this repository does not own.
    /// </summary>
    private static decimal? EstimateCost(LlmOptions? options, IReadOnlyList<Layer2ScenarioResult> results)
    {
        if (options?.PricePerMillionInputTokens is not { } input
            || options.PricePerMillionOutputTokens is not { } output)
        {
            return null;
        }

        var inputTokens = results.Sum(result => result.InputTokens);
        var outputTokens = results.Sum(result => result.OutputTokens);

        return ((inputTokens * input) + (outputTokens * output)) / 1_000_000m;
    }

    private static string SpecVersionOf()
    {
        var path = Path.Combine(RepositoryLayout.Root, "agents", "absence-concierge", "definition.json");
        using var document = JsonDocument.Parse(File.ReadAllText(path));

        return document.RootElement.TryGetProperty("version", out var version)
            ? version.GetString() ?? "unknown"
            : "unknown";
    }
}
