using System.Diagnostics;
using AbsenceConcierge.AgentService.Telemetry;

namespace AbsenceConcierge.Evals.Execution;

/// <summary>
/// One logical tool call. <c>Position</c> is its index in the merged, time-ordered
/// timeline — what <c>order</c> compares — and <c>Attempts</c> counts the transport
/// attempts inside it, which is a different number and a different question
/// (SPEC §2.2.1).
/// </summary>
public sealed record ToolCallRecord(
    int Position,
    string Tool,
    string Kind,
    string Outcome,
    IReadOnlyDictionary<string, string> Arguments,
    IReadOnlyList<string> ResultIds,
    int Attempts,
    IReadOnlyDictionary<string, object?> Tags);

public sealed record TraceEventRecord(int Position, string Name, IReadOnlyDictionary<string, object?> Tags);

/// <summary>
/// One turn's graded result. <c>Index</c> is 1-based, matching a scenario's
/// <c>turn:</c> selector.
/// </summary>
public sealed record TurnRecord(int Index, string Outcome, string TerminationReason, string Reply);

/// <summary>
/// The trace, in the shape assertions are written against.
///
/// <para>
/// This is the whole interface Layer 1 grades. It carries tool calls, contract
/// events and per-turn outcomes — and deliberately nothing that would let an
/// assertion match the agent's prose, except the replies themselves, which exist
/// solely so <c>output_excludes_internal_ids</c> can search them for identifiers.
/// No assertion may ask what the agent <em>said</em> (ADR-0003); the moment one
/// does, every rewording becomes a false regression.
/// </para>
/// </summary>
public sealed class TraceRecording
{
    private TraceRecording(
        IReadOnlyList<ToolCallRecord> toolCalls,
        IReadOnlyList<TraceEventRecord> events,
        IReadOnlyList<TurnRecord> turns)
    {
        ToolCalls = toolCalls;
        Events = events;
        Turns = turns;
    }

    public IReadOnlyList<ToolCallRecord> ToolCalls { get; }

    public IReadOnlyList<TraceEventRecord> Events { get; }

    public IReadOnlyList<TurnRecord> Turns { get; }

    public static TraceRecording From(IEnumerable<Activity> spans, IReadOnlyList<TurnRecord> turns)
    {
        ArgumentNullException.ThrowIfNull(spans);

        // One timeline, so `order` compares tool calls and events on the same ruler.
        // Sorted by start time rather than by export order: a span is exported when it
        // ends, so export order says nothing useful about when things happened.
        var timeline = new List<(DateTime At, Activity Span, ActivityEvent? Event)>();

        foreach (var span in spans)
        {
            if (span.GetTagItem(AgentDiagnostics.Attributes.ToolName) is string)
            {
                timeline.Add((span.StartTimeUtc, span, null));
            }

            foreach (var activityEvent in span.Events)
            {
                // `attempt` lives on the tool span and is counted there, not ordered
                // here — it is transport, not a decision.
                if (!string.Equals(activityEvent.Name, AgentDiagnostics.Events.ToolAttempt, StringComparison.Ordinal))
                {
                    timeline.Add((activityEvent.Timestamp.UtcDateTime, span, activityEvent));
                }
            }
        }

        // OrderBy, not List.Sort: List.Sort is UNSTABLE, and two events emitted
        // inside the same tick carry the same timestamp. An unstable sort is free to
        // invert them, which would make an `order` assertion — the shape C-1 is
        // written in — pass or fail on which way the sort happened to fall. LINQ's
        // OrderBy is documented stable, so equal timestamps keep the order they were
        // recorded in, which is the order they actually happened in.
        timeline = [.. timeline.OrderBy(entry => entry.At)];

        var toolCalls = new List<ToolCallRecord>();
        var events = new List<TraceEventRecord>();

        for (var position = 0; position < timeline.Count; position++)
        {
            var (_, span, activityEvent) = timeline[position];

            if (activityEvent is { } emitted)
            {
                events.Add(new TraceEventRecord(
                    position,
                    emitted.Name,
                    emitted.Tags.ToDictionary(tag => tag.Key, tag => tag.Value, StringComparer.Ordinal)));

                continue;
            }

            toolCalls.Add(new ToolCallRecord(
                position,
                (string)span.GetTagItem(AgentDiagnostics.Attributes.ToolName)!,
                span.GetTagItem(AgentDiagnostics.Attributes.ToolKind) as string ?? "unknown",
                span.GetTagItem(AgentDiagnostics.Attributes.ToolOutcome) as string ?? "unknown",
                ParseArguments(span.GetTagItem(AgentDiagnostics.Attributes.ToolArguments) as string),
                ParseIds(span.GetTagItem(AgentDiagnostics.Attributes.ToolResultIds) as string),
                span.Events.Count(e => string.Equals(
                    e.Name,
                    AgentDiagnostics.Events.ToolAttempt,
                    StringComparison.Ordinal)),
                span.TagObjects.ToDictionary(tag => tag.Key, tag => tag.Value, StringComparer.Ordinal)));
        }

        return new TraceRecording(toolCalls, events, turns);
    }

    /// <summary>The tool span's recorded arguments, which are <c>key=value;key=value</c>.</summary>
    private static IReadOnlyDictionary<string, string> ParseArguments(string? recorded)
    {
        var arguments = new Dictionary<string, string>(StringComparer.Ordinal);

        if (string.IsNullOrEmpty(recorded))
        {
            return arguments;
        }

        foreach (var pair in recorded.Split(';', StringSplitOptions.RemoveEmptyEntries))
        {
            var separator = pair.IndexOf('=', StringComparison.Ordinal);

            if (separator > 0)
            {
                arguments[pair[..separator]] = pair[(separator + 1)..];
            }
        }

        return arguments;
    }

    private static IReadOnlyList<string> ParseIds(string? recorded) =>
        string.IsNullOrEmpty(recorded)
            ? []
            : [.. recorded.Split(';', StringSplitOptions.RemoveEmptyEntries)];
}
