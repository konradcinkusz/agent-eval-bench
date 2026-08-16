using System.Globalization;
using System.Text.Json;
using AbsenceConcierge.Evals.Scenarios;

namespace AbsenceConcierge.Evals.Judging;

/// <param name="Scenario">Scenario id.</param>
/// <param name="Rubric">Criterion name.</param>
/// <param name="Score">The human's score, on the rubric's own scale.</param>
/// <param name="Labeller">Who scored it. A handle, never a name or an email.</param>
/// <param name="Date">When, ISO-8601.</param>
public sealed record HumanLabel(string Scenario, string Rubric, int Score, string Labeller, string Date);

/// <param name="Labels">How many human labels exist at all.</param>
/// <param name="Scenarios">How many distinct scenarios they cover.</param>
/// <param name="Compared">Pairs where a human label and a judge score exist for the same scenario and criterion.</param>
/// <param name="ExactAgreements">Pairs where the two scores are identical.</param>
/// <param name="WithinOne">Pairs differing by at most one level.</param>
/// <param name="Kappa">Cohen's κ, or null when there is not enough to compute one.</param>
/// <param name="Gating">Whether Layer 2's scores are permitted to gate anything yet.</param>
/// <param name="Reason">Why, in a sentence a reader can act on.</param>
public sealed record CalibrationReport(
    int Labels,
    int Scenarios,
    int Compared,
    int ExactAgreements,
    int WithinOne,
    double? Kappa,
    bool Gating,
    string Reason);

/// <summary>
/// The human labels Layer 2 is calibrated against, and the arithmetic that decides
/// whether its scores may gate anything.
///
/// <para>
/// AI-EVALS.md §5 requires a judge to be calibrated against human labels before its
/// scores gate, and marks that rule *"not yet demonstrated in the estate"*. This is
/// the demonstration — including, honestly, the state it is in today: <b>45 labels
/// across 21 scenarios, every one of them written by an AI rater rather than a
/// human</b>, so the judge reports and trends and gates nothing. That state is
/// printed on every run rather than inferred from a passing test, because an
/// uncalibrated judge that silently gates is worse than no judge: it blocks merges
/// on a number nobody has checked against a person — and a label set written by
/// another model is not that person.
/// </para>
/// <para>
/// The protocol, and how to add labels, is <c>docs/CALIBRATION.md</c>.
/// </para>
/// </summary>
public static class Calibration
{
    private static readonly JsonSerializerOptions Json = new() { PropertyNameCaseInsensitive = true };

    public static string LabelsPath =>
        Path.Combine(RepositoryLayout.Root, "evals", "calibration", "labels.jsonl");

    /// <summary>
    /// Reads the labels. One JSON object per line — an append-only format on
    /// purpose, so adding a label is a one-line diff a reviewer can read, and so two
    /// people labelling in parallel do not conflict on every line of a JSON array.
    /// </summary>
    public static IReadOnlyList<HumanLabel> Load()
    {
        if (!File.Exists(LabelsPath))
        {
            return [];
        }

        var labels = new List<HumanLabel>();
        var lineNumber = 0;

        foreach (var line in File.ReadLines(LabelsPath))
        {
            lineNumber++;
            var trimmed = line.Trim();

            if (trimmed.Length == 0 || trimmed.StartsWith("//", StringComparison.Ordinal))
            {
                continue;
            }

            var label = JsonSerializer.Deserialize<HumanLabel>(trimmed, Json)
                ?? throw new InvalidOperationException(
                    $"'{LabelsPath}' line {lineNumber} is not a label. A malformed label is dropped "
                    + "silently by nothing in this repository: it would quietly lower the label count "
                    + "that decides whether the judge may gate.");

            labels.Add(label);
        }

        return labels;
    }

    /// <summary>
    /// Compares the judge's scores against the human labels and decides whether the
    /// judge has earned the right to gate.
    /// </summary>
    public static CalibrationReport Compare(IReadOnlyList<RubricScore> judged, string scenarioId)
    {
        ArgumentNullException.ThrowIfNull(judged);

        var all = Load();

        var pairs = judged
            .Select(score => (
                Judge: score,
                Human: all.FirstOrDefault(label =>
                    string.Equals(label.Scenario, scenarioId, StringComparison.Ordinal)
                    && string.Equals(label.Rubric, score.Rubric, StringComparison.Ordinal))))
            .Where(pair => pair.Human is not null)
            .Select(pair => (pair.Judge.Score, Human: pair.Human!.Score))
            .ToList();

        return Summarise(all, pairs);
    }

    /// <summary>The corpus-wide position, independent of any one scenario.</summary>
    public static CalibrationReport Overall(IReadOnlyDictionary<string, IReadOnlyList<RubricScore>> judged)
    {
        ArgumentNullException.ThrowIfNull(judged);

        var all = Load();
        var pairs = new List<(int Judge, int Human)>();

        foreach (var label in all)
        {
            if (judged.TryGetValue(label.Scenario, out var scores)
                && scores.FirstOrDefault(score =>
                    string.Equals(score.Rubric, label.Rubric, StringComparison.Ordinal)) is { } match)
            {
                pairs.Add((match.Score, label.Score));
            }
        }

        return Summarise(all, pairs);
    }

    private static CalibrationReport Summarise(
        IReadOnlyList<HumanLabel> all,
        IReadOnlyList<(int Judge, int Human)> pairs)
    {
        var gate = JudgeConfiguration.Current.Calibration;
        var scenarios = all.Select(label => label.Scenario).Distinct(StringComparer.Ordinal).Count();

        var exact = pairs.Count(pair => pair.Judge == pair.Human);
        var withinOne = pairs.Count(pair => Math.Abs(pair.Judge - pair.Human) <= 1);
        var kappa = CohenKappa(pairs);

        var reasons = new List<string>();

        if (all.Count < gate.MinimumLabels)
        {
            reasons.Add($"{all.Count} of {gate.MinimumLabels} labels");
        }

        if (scenarios < gate.MinimumScenarios)
        {
            reasons.Add($"{scenarios} of {gate.MinimumScenarios} scenarios covered");
        }

        if (kappa is null)
        {
            reasons.Add("κ not computable from the labels present");
        }
        else if (kappa < gate.MinimumKappa)
        {
            reasons.Add(string.Create(
                CultureInfo.InvariantCulture,
                $"κ {kappa:F2} below the required {gate.MinimumKappa:F2}"));
        }

        var gating = reasons.Count == 0;

        return new CalibrationReport(
            all.Count,
            scenarios,
            pairs.Count,
            exact,
            withinOne,
            kappa,
            gating,
            gating
                ? "Calibrated: Layer 2 scores may gate."
                : $"NOT calibrated — {string.Join("; ", reasons)}. Layer 2 scores are reported and gate nothing.");
    }

    /// <summary>
    /// Cohen's κ, unweighted.
    ///
    /// <para>
    /// Unweighted rather than linear-weighted, deliberately: a weighted κ would give
    /// partial credit for being one level out, and the anchors are written so that
    /// one level out is a real disagreement — the difference between "traceable but
    /// imprecise" and "a claim with no support" is the whole of the grounding
    /// criterion. Raw agreement is reported alongside, because κ punishes a labeller
    /// who is right in a way that is unsurprising, and both numbers are worth seeing.
    /// </para>
    /// <para>
    /// Returns null when there is nothing to compute — fewer than two pairs, or
    /// perfect agreement on a single category, where the expected-agreement term is
    /// 1 and κ is undefined rather than perfect.
    /// </para>
    /// </summary>
    public static double? CohenKappa(IReadOnlyList<(int Judge, int Human)> pairs)
    {
        ArgumentNullException.ThrowIfNull(pairs);

        if (pairs.Count < 2)
        {
            return null;
        }

        var total = (double)pairs.Count;
        var observed = pairs.Count(pair => pair.Judge == pair.Human) / total;

        var categories = pairs
            .SelectMany(pair => new[] { pair.Judge, pair.Human })
            .Distinct()
            .ToList();

        var expected = categories.Sum(category =>
            (pairs.Count(pair => pair.Judge == category) / total)
            * (pairs.Count(pair => pair.Human == category) / total));

        return Math.Abs(1 - expected) < 1e-9 ? null : (observed - expected) / (1 - expected);
    }
}
