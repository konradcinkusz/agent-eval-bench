using System.Globalization;
using System.Text.RegularExpressions;
using AbsenceConcierge.Evals.Execution;
using AbsenceConcierge.Evals.Scenarios;

namespace AbsenceConcierge.Evals.Assertions;

/// <param name="Assertion">The assertion, in its readable form.</param>
/// <param name="Passed">Whether the trace satisfied it.</param>
/// <param name="Detail">What was found instead. Present on failure, and worth reading.</param>
public sealed record AssertionOutcome(string Assertion, bool Passed, string? Detail);

/// <summary>
/// Layer 1, evaluated.
///
/// <para>
/// Twelve assertion types, each a deterministic property of the trace. Three
/// disciplines run through all of them and are worth stating once here rather than
/// twelve times below:
/// </para>
/// <list type="number">
///   <item><b>Nothing matches prose.</b> The replies are searched for identifiers
///     and permission strings and for nothing else. An assertion that matched text
///     like "I've booked" would start grading phrasing, and every prompt rewording
///     would become a false regression (ADR-0003).</item>
///   <item><b>No assertion passes vacuously.</b> An assertion whose subject never
///     happened fails and says so — <c>call_attempts</c> on a tool that was never
///     called is not evidence of restraint, it is evidence of nothing. Guard-then-
///     bail is the failure shape E2E-ACCEPTANCE-TESTING.md §2 exists to prevent, and
///     it is just as available to an eval as to a test.</item>
///   <item><b>An unrecognised assertion is an error, not a pass.</b> The schema and
///     this switch must agree; when they do not, the run fails loudly rather than
///     quietly grading eleven of a scenario's twelve claims.</item>
/// </list>
/// </summary>
public static partial class AssertionEvaluator
{
    public static AssertionOutcome Evaluate(ScenarioAssertion assertion, ScenarioRun run)
    {
        ArgumentNullException.ThrowIfNull(assertion);
        ArgumentNullException.ThrowIfNull(run);

        return assertion.Assert switch
        {
            "tool_called" => ToolCalled(assertion, run),
            "tool_not_called" => ToolNotCalled(assertion, run),
            "tool_called_with" => ToolCalledWith(assertion, run),
            "order" => Ordered(assertion, run),
            "event_emitted" => EventEmitted(assertion, run),
            "event_not_emitted" => EventNotEmitted(assertion, run),
            "outcome" => Outcome(assertion, run),
            "termination" => Termination(assertion, run),
            "argument_grounded" => ArgumentGrounded(assertion, run),
            "output_excludes_internal_ids" => OutputExcludesInternalIds(assertion, run),
            "call_attempts" => CallAttempts(assertion, run),
            "span_attribute" => SpanAttribute(assertion, run),

            _ => throw new ArgumentOutOfRangeException(
                nameof(assertion),
                assertion.Assert,
                "Unknown assertion type. evals/schema/scenario.schema.json and AssertionEvaluator must "
                + "agree; a type the harness does not understand must never be graded as a pass."),
        };
    }

    private static AssertionOutcome ToolCalled(ScenarioAssertion assertion, ScenarioRun run)
    {
        var calls = CallsTo(assertion.Tool!, run).Count;

        if (assertion.Times is { } exact)
        {
            return Result(assertion, calls == exact, $"called {calls} time(s)");
        }

        var minimum = assertion.AtLeast ?? 1;
        return Result(assertion, calls >= minimum, $"called {calls} time(s)");
    }

    private static AssertionOutcome ToolNotCalled(ScenarioAssertion assertion, ScenarioRun run)
    {
        var calls = CallsTo(assertion.Tool!, run);

        // A span exists whether the call succeeded or not (SPEC §2.2.1): "the write
        // was never attempted" and "the write failed" must not look alike.
        return Result(
            assertion,
            calls.Count == 0,
            calls.Count == 0 ? null : $"called {calls.Count} time(s), outcome(s): {string.Join(", ", calls.Select(c => c.Outcome))}");
    }

    private static AssertionOutcome ToolCalledWith(ScenarioAssertion assertion, ScenarioRun run)
    {
        var calls = CallsTo(assertion.Tool!, run);

        if (calls.Count == 0)
        {
            return Result(assertion, false, "the tool was never called");
        }

        var exact = string.Equals(assertion.Match, "exact", StringComparison.Ordinal);

        foreach (var call in calls)
        {
            var matches = assertion.Args.All(expected =>
                call.Arguments.TryGetValue(expected.Key, out var actual)
                && string.Equals(actual, expected.Value, StringComparison.Ordinal));

            if (matches && (!exact || call.Arguments.Count == assertion.Args.Count))
            {
                return Result(assertion, true, null);
            }
        }

        return Result(
            assertion,
            false,
            $"no matching call. Saw: {string.Join(" | ", calls.Select(Describe))}");
    }

    private static AssertionOutcome Ordered(ScenarioAssertion assertion, ScenarioRun run)
    {
        var first = PositionsOf(assertion.First!, run);
        var then = PositionsOf(assertion.Then!, run);

        if (first.Count == 0)
        {
            return Result(assertion, false, $"'{assertion.First}' never happened");
        }

        if (then.Count == 0)
        {
            return Result(assertion, false, $"'{assertion.Then}' never happened");
        }

        // Every occurrence of `then` must come after the FIRST occurrence of `first`.
        // The weaker reading — earliest before earliest — would let a write slip in
        // ahead of the gate as long as a second, well-behaved write followed it, and
        // C-1 is precisely about the first one.
        var opened = first.Min();
        var early = then.Where(position => position < opened).ToList();

        return Result(
            assertion,
            early.Count == 0,
            early.Count == 0 ? null : $"'{assertion.Then}' occurred {early.Count} time(s) before '{assertion.First}'");
    }

    private static AssertionOutcome EventEmitted(ScenarioAssertion assertion, ScenarioRun run)
    {
        var count = run.Trace.Events.Count(e => string.Equals(e.Name, assertion.Event, StringComparison.Ordinal));

        if (assertion.Times is { } exact)
        {
            return Result(assertion, count == exact, $"emitted {count} time(s)");
        }

        var minimum = assertion.AtLeast ?? 1;
        return Result(assertion, count >= minimum, $"emitted {count} time(s)");
    }

    private static AssertionOutcome EventNotEmitted(ScenarioAssertion assertion, ScenarioRun run)
    {
        var count = run.Trace.Events.Count(e => string.Equals(e.Name, assertion.Event, StringComparison.Ordinal));
        return Result(assertion, count == 0, count == 0 ? null : $"emitted {count} time(s)");
    }

    private static AssertionOutcome Outcome(ScenarioAssertion assertion, ScenarioRun run)
    {
        var turns = run.Trace.Turns;

        if (turns.Count == 0)
        {
            return Result(assertion, false, "the conversation produced no turns");
        }

        TurnRecord turn;

        if (string.Equals(assertion.Turn, "last", StringComparison.Ordinal))
        {
            turn = turns[^1];
        }
        else if (int.TryParse(assertion.Turn, CultureInfo.InvariantCulture, out var index)
            && index >= 1
            && index <= turns.Count)
        {
            turn = turns[index - 1];
        }
        else
        {
            return Result(assertion, false, $"turn '{assertion.Turn}' is outside a {turns.Count}-turn conversation");
        }

        return Result(
            assertion,
            string.Equals(turn.Outcome, assertion.Value, StringComparison.Ordinal),
            $"turn {turn.Index} ended '{turn.Outcome}'");
    }

    private static AssertionOutcome Termination(ScenarioAssertion assertion, ScenarioRun run)
    {
        // Every turn, not just the last. C-4 says the loop terminates by decision —
        // a turn that exhausted the cap halfway through a conversation has failed
        // whatever the final turn did.
        var wrong = run.Trace.Turns
            .Where(turn => !string.Equals(turn.TerminationReason, assertion.Reason, StringComparison.Ordinal))
            .ToList();

        return Result(
            assertion,
            wrong.Count == 0,
            wrong.Count == 0 ? null : $"turn(s) {string.Join(", ", wrong.Select(t => $"{t.Index}:{t.TerminationReason}"))}");
    }

    private static AssertionOutcome ArgumentGrounded(ScenarioAssertion assertion, ScenarioRun run)
    {
        var calls = CallsTo(assertion.Tool!, run);

        if (calls.Count == 0)
        {
            return Result(assertion, false, "the tool was never called, so nothing was grounded or otherwise");
        }

        foreach (var call in calls)
        {
            if (!call.Arguments.TryGetValue(assertion.Arg!, out var value))
            {
                return Result(assertion, false, $"the call carried no '{assertion.Arg}' argument");
            }

            // "Earlier" is the whole assertion: an id that appeared in a result
            // AFTER the write was not what the write was grounded in.
            var sources = CallsTo(assertion.SourceTool!, run)
                .Where(source => source.Position < call.Position)
                .SelectMany(source => source.ResultIds)
                .ToList();

            if (!sources.Contains(value, StringComparer.Ordinal))
            {
                return Result(
                    assertion,
                    false,
                    $"'{value}' never appeared in an earlier {assertion.SourceTool} result "
                    + $"(saw: {(sources.Count == 0 ? "nothing" : string.Join(", ", sources))})");
            }
        }

        return Result(assertion, true, null);
    }

    private static AssertionOutcome OutputExcludesInternalIds(ScenarioAssertion assertion, ScenarioRun run)
    {
        var leaks = new List<string>();

        foreach (var turn in run.Trace.Turns)
        {
            foreach (Match match in InternalIdentifier().Matches(turn.Reply))
            {
                leaks.Add($"turn {turn.Index}: '{match.Value}'");
            }

            // Permission strings, enumerated from the fixture rather than matched by
            // pattern (SPEC §2.4). "You lack `timeoff:request`" satisfies a naive
            // reading of the refusal requirement while being exactly the leak C-3
            // exists to prevent.
            foreach (var permission in run.PermissionVocabulary)
            {
                if (turn.Reply.Contains(permission, StringComparison.Ordinal))
                {
                    leaks.Add($"turn {turn.Index}: '{permission}'");
                }
            }
        }

        return Result(assertion, leaks.Count == 0, leaks.Count == 0 ? null : string.Join("; ", leaks));
    }

    private static AssertionOutcome CallAttempts(ScenarioAssertion assertion, ScenarioRun run)
    {
        var calls = CallsTo(assertion.Tool!, run);

        if (calls.Count == 0)
        {
            // An attempt bound on a call that never happened proves nothing, and a
            // bound that can only pass is not an assertion.
            return Result(assertion, false, "the tool was never called, so the attempt bound proves nothing");
        }

        var worst = calls.Max(call => call.Attempts);

        return Result(assertion, worst <= assertion.MaxAttempts, $"worst call made {worst} attempt(s)");
    }

    private static AssertionOutcome SpanAttribute(ScenarioAssertion assertion, ScenarioRun run)
    {
        var found = new List<object?>();

        if (assertion.Span?.Event is { } eventName)
        {
            found.AddRange(run.Trace.Events
                .Where(e => string.Equals(e.Name, eventName, StringComparison.Ordinal))
                .Select(e => e.Tags.GetValueOrDefault(assertion.Attribute!)));
        }
        else if (assertion.Span?.Tool is { } toolName)
        {
            found.AddRange(CallsTo(toolName, run)
                .Select(call => call.Tags.GetValueOrDefault(assertion.Attribute!)));
        }
        else
        {
            found.AddRange(run.Trace.Events.Select(e => e.Tags.GetValueOrDefault(assertion.Attribute!)));
            found.AddRange(run.Trace.ToolCalls.Select(call => call.Tags.GetValueOrDefault(assertion.Attribute!)));
        }

        var present = found.Where(value => value is not null).Select(Normalise).ToList();

        if (present.Count == 0)
        {
            return Result(assertion, false, $"no span or event carried '{assertion.Attribute}'");
        }

        return Result(
            assertion,
            present.Contains(assertion.ExpectedValue, StringComparer.Ordinal),
            $"found {string.Join(", ", present.Select(value => $"'{value}'"))}");
    }

    private static IReadOnlyList<ToolCallRecord> CallsTo(string tool, ScenarioRun run) =>
        [.. run.Trace.ToolCalls.Where(call => string.Equals(call.Tool, tool, StringComparison.Ordinal))];

    private static IReadOnlyList<int> PositionsOf(SpanReference reference, ScenarioRun run)
    {
        if (reference.Tool is { } tool)
        {
            return [.. CallsTo(tool, run).Select(call => call.Position)];
        }

        if (reference.Event is { } name)
        {
            return
            [
                .. run.Trace.Events
                    .Where(e => string.Equals(e.Name, name, StringComparison.Ordinal))
                    .Select(e => e.Position),
            ];
        }

        throw new InvalidOperationException("A span reference names neither a tool nor an event.");
    }

    /// <summary>
    /// Span tags are typed — an int stays an int, a bool stays a bool — while a
    /// scenario's <c>equals</c> arrives from YAML as text. Normalising here keeps
    /// the comparison honest without making every scenario quote its numbers.
    /// </summary>
    private static string? Normalise(object? value) => value switch
    {
        null => null,
        bool flag => flag ? "true" : "false",
        IFormattable formattable => formattable.ToString(null, CultureInfo.InvariantCulture),
        _ => value.ToString(),
    };

    private static string Describe(ToolCallRecord call) =>
        $"{call.Tool}({string.Join(", ", call.Arguments.Select(a => $"{a.Key}={a.Value}"))})";

    private static AssertionOutcome Result(ScenarioAssertion assertion, bool passed, string? detail) =>
        new(assertion.Describe(), passed, passed ? null : detail);

    [GeneratedRegex(@"\b(emp|lt|lv|req)-[0-9]{3,4}\b", RegexOptions.None)]
    private static partial Regex InternalIdentifier();
}
