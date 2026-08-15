using System.Globalization;
using System.Text;
using AbsenceConcierge.Evals.Scenarios;

namespace AbsenceConcierge.Evals.Extraction;

/// <summary>
/// Writes a <see cref="ScenarioFile"/> as the YAML the corpus holds.
///
/// <para>
/// Hand-rolled rather than serialised through YamlDotNet, for the same reason
/// <c>FixtureComposer</c> merges fixtures at the node level: a round trip through
/// .NET types loses scalar styles, and <c>start_date: 2026-08-11</c> unquoted is a
/// YAML timestamp rather than the string every assertion compares against. It also
/// buys the header — an extracted scenario arrives with the three things a human has
/// to do to it written at the top, where they cannot be missed.
/// </para>
/// </summary>
public static class ScenarioYaml
{
    public static string Write(ScenarioFile scenario)
    {
        ArgumentNullException.ThrowIfNull(scenario);

        var yaml = new StringBuilder();

        yaml.AppendLine("# yaml-language-server: $schema=../../schema/scenario.schema.json")
            .AppendLine("#")
            .AppendLine("# Extracted from a recorded trace by ScenarioExtractor. Every assertion below was")
            .AppendLine("# derived from what the trace contained — including the tool_not_called lines, which")
            .AppendLine("# are the half a person reconstructing an incident forgets.")
            .AppendLine("#")
            .AppendLine("# Before this belongs in the corpus:")
            .AppendLine("#   1. Replace both REVIEW: markers. scripts/validate-scenarios.mjs rejects them, so")
            .AppendLine("#      this file cannot be merged while they are here.")
            .AppendLine("#   2. Decide whether the behaviour asserted below is the behaviour you want. An")
            .AppendLine("#      extracted scenario records what happened; if what happened was the bug, the")
            .AppendLine("#      assertions are what should change, and then the agent.")
            .AppendLine("#   3. Add the rubrics this turn should be judged against, if any.")
            .AppendLine();

        Scalar(yaml, 0, "id", scenario.Id);
        Scalar(yaml, 0, "class", scenario.Class);
        Scalar(yaml, 0, "gate", scenario.Gate);
        Scalar(yaml, 0, "title", scenario.Title);
        yaml.AppendLine();

        yaml.AppendLine("why: >-");
        foreach (var line in Wrap(scenario.Why, 76))
        {
            yaml.Append("  ").AppendLine(line);
        }

        yaml.AppendLine();

        if (scenario.Origin is { } origin)
        {
            yaml.AppendLine("origin:");
            Scalar(yaml, 1, "kind", origin.Kind);

            if (!string.IsNullOrWhiteSpace(origin.Reference))
            {
                Scalar(yaml, 1, "reference", origin.Reference);
            }

            if (!string.IsNullOrWhiteSpace(origin.Date))
            {
                Scalar(yaml, 1, "date", origin.Date);
            }

            yaml.AppendLine();
        }

        WriteFixture(yaml, scenario.Fixture);
        WriteConversation(yaml, scenario.Conversation);
        WriteExpectations(yaml, scenario.Expect);

        if (scenario.Rubrics.Count > 0)
        {
            yaml.AppendLine().AppendLine("rubrics:");

            foreach (var rubric in scenario.Rubrics)
            {
                yaml.Append("  - ").AppendLine(rubric);
            }
        }

        return yaml.ToString();
    }

    private static void WriteFixture(StringBuilder yaml, ScenarioFixture fixture)
    {
        yaml.AppendLine("fixture:");
        Scalar(yaml, 1, "base", fixture.Base);
        Scalar(yaml, 1, "clock", fixture.Clock);
        Scalar(yaml, 1, "timezone", fixture.Timezone);

        if (!string.IsNullOrWhiteSpace(fixture.Locale))
        {
            Scalar(yaml, 1, "locale", fixture.Locale);
        }

        if (fixture.ToolBehaviour.Count > 0)
        {
            yaml.AppendLine("  tool_behaviour:");

            foreach (var (tool, behaviour) in fixture.ToolBehaviour.OrderBy(pair => pair.Key, StringComparer.Ordinal))
            {
                yaml.Append("    ").Append(tool).AppendLine(":");
                Scalar(yaml, 3, "outcome", behaviour.Outcome);

                if (behaviour.AfterCalls > 0)
                {
                    Number(yaml, 3, "after_calls", behaviour.AfterCalls);
                }

                if (behaviour.LatencyMs > 0)
                {
                    Number(yaml, 3, "latency_ms", behaviour.LatencyMs);
                }
            }
        }

        // The `overrides` block is not emitted, and cannot be: it is merged at the
        // YAML node level and never reaches a .NET type (see ScenarioFixture). A trace
        // from a world that differed from the base fixture needs that delta added by
        // hand, and saying so here is cheaper than a reader discovering it from a
        // scenario that replays against the wrong world.
        yaml.AppendLine();
    }

    private static void WriteConversation(StringBuilder yaml, IReadOnlyList<ScenarioTurn> conversation)
    {
        yaml.AppendLine("conversation:");

        foreach (var turn in conversation)
        {
            yaml.Append("  - role: ").AppendLine(turn.Role);

            if (!string.IsNullOrWhiteSpace(turn.Decision))
            {
                yaml.Append("    decision: ").AppendLine(turn.Decision);
            }

            yaml.Append("    content: ").AppendLine(Quote(turn.Content));
        }

        yaml.AppendLine();
    }

    private static void WriteExpectations(StringBuilder yaml, IReadOnlyList<ScenarioAssertion> expectations)
    {
        yaml.AppendLine("expect:");

        foreach (var assertion in expectations)
        {
            yaml.Append("  - assert: ").AppendLine(assertion.Assert);

            Optional(yaml, "tool", assertion.Tool);
            Optional(yaml, "event", assertion.Event);
            OptionalNumber(yaml, "times", assertion.Times);
            OptionalNumber(yaml, "at_least", assertion.AtLeast);
            OptionalNumber(yaml, "max_attempts", assertion.MaxAttempts);
            Optional(yaml, "arg", assertion.Arg);
            Optional(yaml, "source_tool", assertion.SourceTool);
            Optional(yaml, "reason", assertion.Reason);
            Optional(yaml, "value", assertion.Value);

            if (string.Equals(assertion.Assert, "outcome", StringComparison.Ordinal))
            {
                yaml.Append("    turn: ").AppendLine(assertion.Turn);
            }

            if (assertion.Args.Count > 0)
            {
                yaml.Append("    match: ").AppendLine(assertion.Match);
                yaml.AppendLine("    args:");

                foreach (var (key, value) in assertion.Args.OrderBy(pair => pair.Key, StringComparer.Ordinal))
                {
                    // Always quoted. Every value here is compared as a string, and an
                    // unquoted 2026-08-11 is a date to a YAML parser.
                    yaml.Append("      ").Append(key).Append(": ").AppendLine(Quote(value));
                }
            }

            Reference(yaml, "first", assertion.First);
            Reference(yaml, "then", assertion.Then);
            Reference(yaml, "span", assertion.Span);
        }
    }

    private static void Reference(StringBuilder yaml, string key, SpanReference? reference)
    {
        if (reference is null)
        {
            return;
        }

        var inner = reference.Tool is { } tool ? $"tool: {tool}" : $"event: {reference.Event}";
        yaml.Append("    ").Append(key).Append(": { ").Append(inner).AppendLine(" }");
    }

    private static void Optional(StringBuilder yaml, string key, string? value)
    {
        if (!string.IsNullOrWhiteSpace(value))
        {
            yaml.Append("    ").Append(key).Append(": ").AppendLine(value);
        }
    }

    private static void OptionalNumber(StringBuilder yaml, string key, int? value)
    {
        if (value is { } number)
        {
            yaml.Append("    ").Append(key).Append(": ").AppendLine(number.ToString(CultureInfo.InvariantCulture));
        }
    }

    private static void Scalar(StringBuilder yaml, int depth, string key, string? value) =>
        yaml.Append(new string(' ', depth * 2)).Append(key).Append(": ").AppendLine(Quote(value ?? string.Empty));

    private static void Number(StringBuilder yaml, int depth, string key, int value) =>
        yaml.Append(new string(' ', depth * 2))
            .Append(key)
            .Append(": ")
            .AppendLine(value.ToString(CultureInfo.InvariantCulture));

    /// <summary>
    /// Single-quotes anything a YAML parser might read as something other than a
    /// string. Erring towards quoting: an over-quoted scalar is ugly, and an
    /// under-quoted one is a date where an identifier was meant.
    /// </summary>
    private static string Quote(string value)
    {
        if (value.Length == 0)
        {
            return "''";
        }

        var plain = value.All(character => char.IsLetterOrDigit(character) || character is '_' or '-' or '.' or '/')
            && !char.IsDigit(value[0]);

        return plain ? value : $"'{value.Replace("'", "''", StringComparison.Ordinal)}'";
    }

    private static IEnumerable<string> Wrap(string text, int width)
    {
        var line = new StringBuilder();

        foreach (var word in text.Split(' ', StringSplitOptions.RemoveEmptyEntries))
        {
            if (line.Length > 0 && line.Length + 1 + word.Length > width)
            {
                yield return line.ToString();
                line.Clear();
            }

            if (line.Length > 0)
            {
                line.Append(' ');
            }

            line.Append(word);
        }

        if (line.Length > 0)
        {
            yield return line.ToString();
        }
    }
}
