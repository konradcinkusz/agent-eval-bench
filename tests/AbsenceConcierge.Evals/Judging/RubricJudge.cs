using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;
using AbsenceConcierge.AgentService.Agent.Llm;

namespace AbsenceConcierge.Evals.Judging;

/// <param name="Rubric">The criterion, exactly as the scenario named it.</param>
/// <param name="Score">An integer within the rubric's scale.</param>
/// <param name="Justification">One sentence, citing what was observed.</param>
public sealed record RubricScore(string Rubric, int Score, string Justification);

/// <param name="Scores">One per criterion asked for.</param>
/// <param name="Model">The model that actually answered, as the service reported it.</param>
/// <param name="InputTokens">Metered: SPEC §8.1 budgets Layer 2 in money as well as minutes.</param>
/// <param name="OutputTokens">As above.</param>
public sealed record JudgeVerdict(
    IReadOnlyList<RubricScore> Scores,
    string Model,
    int InputTokens,
    int OutputTokens);

public interface IRubricJudge
{
    ValueTask<JudgeVerdict> ScoreAsync(
        string prompt,
        IReadOnlyList<string> rubrics,
        CancellationToken cancellationToken = default);
}

/// <summary>
/// Asks a model for scores, and refuses anything that is not one.
///
/// <para>
/// <b>Parsing is strict on purpose.</b> A judge that returned prose, or a decimal, or
/// a criterion nobody asked for, has not produced a measurement — and coercing it
/// into one ("it said 'quite good', call that a 2") invents the number the whole
/// layer exists to avoid. Every rejection below raises an error the run reports as a
/// judge failure, which is a different fact from the agent scoring badly and is
/// recorded as one.
/// </para>
/// </summary>
public sealed class RubricJudge(ILlmProvider provider, JudgeConfiguration configuration) : IRubricJudge
{
    private static readonly JsonSerializerOptions Json = new()
    {
        PropertyNameCaseInsensitive = true,
        NumberHandling = JsonNumberHandling.Strict,
    };

    public async ValueTask<JudgeVerdict> ScoreAsync(
        string prompt,
        IReadOnlyList<string> rubrics,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(rubrics);

        var response = await provider
            .CompleteAsync(
                new LlmRequest(
                    prompt,
                    [new LlmMessage("user", "Score the conversation above. Return only the JSON object.")],
                    MaxOutputTokens: 900),
                cancellationToken)
            .ConfigureAwait(false);

        var scores = Parse(response.Text, rubrics);

        return new JudgeVerdict(scores, response.Model, response.InputTokens, response.OutputTokens);
    }

    /// <summary>
    /// Reads the judge's reply, or explains precisely why it could not be read.
    /// Exposed so the machinery can be tested against recorded replies with no
    /// credential — which is the only way this code is exercised in a repository
    /// that ships without one.
    /// </summary>
    public static IReadOnlyList<RubricScore> Parse(string text, IReadOnlyList<string> expected)
    {
        ArgumentNullException.ThrowIfNull(expected);

        var json = ExtractObject(text);

        JudgeResponse? parsed;

        try
        {
            parsed = JsonSerializer.Deserialize<JudgeResponse>(json, Json);
        }
        catch (JsonException exception)
        {
            throw new InvalidOperationException(
                $"The judge did not return readable JSON: {exception.Message}. Received: {Truncate(text)}",
                exception);
        }

        if (parsed?.Scores is not { Count: > 0 } returned)
        {
            throw new InvalidOperationException(
                $"The judge returned no scores. Received: {Truncate(text)}");
        }

        var configuration = JudgeConfiguration.Current;
        var scores = new List<RubricScore>();

        foreach (var name in expected)
        {
            var match = returned.SingleOrDefault(score =>
                string.Equals(score.Rubric, name, StringComparison.OrdinalIgnoreCase))
                ?? throw new InvalidOperationException(
                    $"The judge did not score '{name}'. A missing criterion is not a zero and is not a "
                    + "pass; it is a run that did not measure what it was asked to measure.");

            var rubric = configuration[name];

            if (match.Score < 0 || match.Score > rubric.Scale)
            {
                throw new InvalidOperationException(
                    $"The judge scored '{name}' as {match.Score}, outside its 0–{rubric.Scale} scale. "
                    + "A score off the scale has no anchor behind it and cannot be compared to a human label.");
            }

            if (string.IsNullOrWhiteSpace(match.Justification))
            {
                throw new InvalidOperationException(
                    $"The judge scored '{name}' with no justification. An unjustified score cannot be "
                    + "reviewed, and calibration is a review.");
            }

            scores.Add(new RubricScore(name, match.Score, match.Justification.Trim()));
        }

        var extra = returned
            .Select(score => score.Rubric)
            .Where(name => !expected.Contains(name, StringComparer.OrdinalIgnoreCase))
            .ToList();

        if (extra.Count > 0)
        {
            // Not merely untidy. A judge inventing criteria is a judge that has
            // stopped following the rubric file, and the rubric file is the pin.
            throw new InvalidOperationException(
                $"The judge scored criteria nobody asked for: {string.Join(", ", extra)}.");
        }

        return scores;
    }

    /// <summary>
    /// Pulls the JSON object out of the reply. Models sometimes wrap it in a fenced
    /// block even when told not to; that is worth tolerating, because it changes no
    /// score. Prose <em>instead of</em> an object is not tolerated, and fails above.
    /// </summary>
    private static string ExtractObject(string text)
    {
        var trimmed = (text ?? string.Empty).Trim();

        var start = trimmed.IndexOf('{', StringComparison.Ordinal);
        var end = trimmed.LastIndexOf('}');

        return start >= 0 && end > start ? trimmed[start..(end + 1)] : trimmed;
    }

    private static string Truncate(string? value) =>
        value is null ? "(nothing)"
        : value.Length <= 300 ? value
        : string.Create(CultureInfo.InvariantCulture, $"{value[..300]}…");

    private sealed record JudgeResponse(IReadOnlyList<RawScore>? Scores);

    private sealed record RawScore(string? Rubric, int Score, string? Justification);
}
