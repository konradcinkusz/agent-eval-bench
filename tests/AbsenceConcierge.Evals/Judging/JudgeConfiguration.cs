using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using AbsenceConcierge.Evals.Scenarios;
using YamlDotNet.Serialization;
using YamlDotNet.Serialization.NamingConventions;

namespace AbsenceConcierge.Evals.Judging;

/// <summary>One criterion, with an anchor per level.</summary>
public sealed class RubricDefinition
{
    public int Scale { get; set; }

    /// <summary>Mean across scenarios must reach this.</summary>
    public double Threshold { get; set; }

    /// <summary>When set, no single score may fall below it. Only <c>grounding</c> has one.</summary>
    public int? Floor { get; set; }

    public string AppliesTo { get; set; } = "any";

    public string Summary { get; set; } = string.Empty;

    /// <summary>Score → what that score means. The reason a number here is regressable.</summary>
    public Dictionary<int, string> Anchors { get; set; } = [];
}

public sealed class SmokeEntry
{
    public string Id { get; set; } = string.Empty;
    public string Why { get; set; } = string.Empty;
}

/// <summary>
/// The bar calibration must clear before Layer 2's scores are allowed to gate
/// anything (AI-EVALS.md §5, and docs/CALIBRATION.md for the worked protocol).
/// </summary>
public sealed class CalibrationGate
{
    public int MinimumLabels { get; set; } = 40;
    public int MinimumScenarios { get; set; } = 8;
    public double MinimumKappa { get; set; } = 0.6;

    /// <summary>
    /// The handle whose labels count as human. Empty — the default — means no human
    /// has labelled anything, and the gate cannot open.
    ///
    /// <para>
    /// This exists because the gate counted labels and never asked who wrote them.
    /// <c>CALIBRATION.md</c> says the gate "additionally waits for human labels under
    /// the owner's own handle" and FINDINGS §5 repeats that the label/scenario/κ
    /// thresholds are "necessary but no longer sufficient" — but
    /// <see cref="HumanLabel.Labeller"/> was deserialised and read nowhere. All 45
    /// labels in <c>evals/calibration/labels.jsonl</c> carry an AI handle and already
    /// clear the first two conditions, so the first keyed run computing κ ≥ 0.6
    /// against them would have certified the judge with the very labels three
    /// documents say cannot certify it.
    /// </para>
    /// <para>
    /// An allow-list rather than a deny-list of model names, deliberately: a list of
    /// known-AI handles goes stale the day a new model ships, and going stale here
    /// means opening the gate. Naming the one handle that counts fails closed.
    /// </para>
    /// </summary>
    public string OwnerHandle { get; set; } = string.Empty;
}

/// <summary>
/// The judge's rubrics, prompt and pull-request subset, loaded from
/// <c>evals/rubrics/</c> — plus the hashes that make "which judge graded this" a
/// fact in the report rather than a guess.
///
/// <para>
/// AI-EVALS.md §5 requires the judge to be pinned by model <em>and</em> prompt. The
/// model pin lives in configuration, because a deployment name is
/// environment-specific and does not belong in a public repository; what is recorded
/// instead is the model the service said actually answered. The prompt pin lives
/// here, as a hash: an edited rubric is a changed instrument, and a score compared
/// across that edit is a measuring stick that changed length between readings.
/// </para>
/// </summary>
public sealed class JudgeConfiguration
{
    private const string RubricsToken = "{{RUBRICS}}";
    private const string TranscriptToken = "{{TRANSCRIPT}}";

    private static readonly IDeserializer Deserializer = new DeserializerBuilder()
        .WithNamingConvention(UnderscoredNamingConvention.Instance)
        .IgnoreUnmatchedProperties()
        .Build();

    private static readonly Lazy<JudgeConfiguration> Instance = new(Load);

    private JudgeConfiguration(
        string version,
        IReadOnlyDictionary<string, RubricDefinition> rubrics,
        IReadOnlyList<SmokeEntry> smoke,
        CalibrationGate calibration,
        string promptTemplate,
        string rubricsHash,
        string promptHash)
    {
        Version = version;
        Rubrics = rubrics;
        Smoke = smoke;
        Calibration = calibration;
        PromptTemplate = promptTemplate;
        RubricsHash = rubricsHash;
        PromptHash = promptHash;
    }

    /// <summary>What calibration must reach before these scores may gate anything.</summary>
    public CalibrationGate Calibration { get; }

    public static JudgeConfiguration Current => Instance.Value;

    public string Version { get; }

    public IReadOnlyDictionary<string, RubricDefinition> Rubrics { get; }

    public IReadOnlyList<SmokeEntry> Smoke { get; }

    public string PromptTemplate { get; }

    /// <summary>SHA-256 of <c>judge.yaml</c>, first 12 hex characters.</summary>
    public string RubricsHash { get; }

    /// <summary>SHA-256 of <c>judge-prompt.md</c>, first 12 hex characters.</summary>
    public string PromptHash { get; }

    public RubricDefinition this[string name] =>
        Rubrics.TryGetValue(name, out var rubric)
            ? rubric
            : throw new KeyNotFoundException(
                $"Scenario names rubric '{name}', which evals/rubrics/judge.yaml does not define. "
                + "A criterion nobody defined cannot be scored, and scoring it anyway would invent "
                + "an anchor the calibration protocol has never seen.");

    /// <summary>
    /// The full system prompt for one scenario: the shared instructions, the
    /// anchors for exactly the criteria this scenario asks for, and the transcript.
    /// </summary>
    public string BuildPrompt(IEnumerable<string> rubricNames, string transcript)
    {
        ArgumentNullException.ThrowIfNull(rubricNames);

        var text = new StringBuilder();

        foreach (var name in rubricNames)
        {
            var rubric = this[name];

            text.Append(CultureInfo.InvariantCulture, $"### `{name}` (0–{rubric.Scale})")
                .AppendLine()
                .AppendLine()
                .AppendLine(rubric.Summary.Trim())
                .AppendLine();

            // Highest first, so the reader meets the standard before the failures.
            foreach (var (score, anchor) in rubric.Anchors.OrderByDescending(anchor => anchor.Key))
            {
                text.Append(CultureInfo.InvariantCulture, $"- **{score}** — {anchor.Trim()}").AppendLine();
            }

            text.AppendLine();
        }

        return PromptTemplate
            .Replace(RubricsToken, text.ToString().TrimEnd(), StringComparison.Ordinal)
            .Replace(TranscriptToken, transcript, StringComparison.Ordinal);
    }

    private static JudgeConfiguration Load()
    {
        var directory = Path.Combine(RepositoryLayout.Root, "evals", "rubrics");
        var rubricsPath = Path.Combine(directory, "judge.yaml");
        var promptPath = Path.Combine(directory, "judge-prompt.md");

        foreach (var path in new[] { rubricsPath, promptPath })
        {
            if (!File.Exists(path))
            {
                throw new FileNotFoundException($"The judge is not fully defined: '{path}' is missing.", path);
            }
        }

        var rubricsText = File.ReadAllText(rubricsPath);
        var promptText = File.ReadAllText(promptPath);

        var file = Deserializer.Deserialize<JudgeFile>(rubricsText)
            ?? throw new InvalidOperationException($"'{rubricsPath}' is empty.");

        if (file.Rubrics.Count == 0)
        {
            throw new InvalidOperationException(
                $"'{rubricsPath}' defines no rubrics. A judge with no criteria scores everything "
                + "perfectly, which is the most confident thing in the repository.");
        }

        foreach (var (name, rubric) in file.Rubrics)
        {
            // An anchor per level, or the scale is decoration. This is the check that
            // stops "0–3" meaning "somewhere between bad and good".
            var expected = Enumerable.Range(0, rubric.Scale + 1).ToList();
            var missing = expected.Except(rubric.Anchors.Keys).ToList();

            if (missing.Count > 0)
            {
                throw new InvalidOperationException(
                    $"Rubric '{name}' is scored 0–{rubric.Scale} but has no anchor for "
                    + $"{string.Join(", ", missing)}. Every level needs a description a second reader "
                    + "could agree or disagree with, or calibration has nothing to measure.");
            }
        }

        return new JudgeConfiguration(
            file.Version,
            file.Rubrics,
            file.Smoke,
            file.Calibration,
            promptText,
            Fingerprint(rubricsText),
            Fingerprint(promptText));
    }

    /// <summary>
    /// Twelve hex characters of SHA-256 — enough to notice a change, short enough to
    /// read in a report. Newlines are normalised so a checkout on a different
    /// platform does not report a different judge.
    /// </summary>
    private static string Fingerprint(string content)
    {
        var normalised = content.Replace("\r\n", "\n", StringComparison.Ordinal);
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(normalised));

        return Convert.ToHexStringLower(hash)[..12];
    }

    private sealed class JudgeFile
    {
        public string Version { get; set; } = "0.0.0";
        public Dictionary<string, RubricDefinition> Rubrics { get; set; } = [];
        public List<SmokeEntry> Smoke { get; set; } = [];
        public CalibrationGate Calibration { get; set; } = new();
    }
}
