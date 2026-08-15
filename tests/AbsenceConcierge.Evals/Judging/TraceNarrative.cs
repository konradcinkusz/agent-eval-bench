using System.Globalization;
using System.Text;
using AbsenceConcierge.Evals.Execution;
using AbsenceConcierge.Evals.Scenarios;

namespace AbsenceConcierge.Evals.Judging;

/// <summary>
/// Renders a run into the text the judge reads.
///
/// <para>
/// <b>The judge sees the trace, not just the reply</b> (SPEC §5), and this is the
/// method that makes that true. A reply can be fluent, warm and well-structured
/// while asserting a fact no tool ever returned; a judge given only the prose grades
/// fluency and calls it grounding. Tool calls, their arguments, the identifiers that
/// came back and the events emitted all go in.
/// </para>
/// <para>
/// Deterministic by construction: the same run renders to the same bytes. A
/// transcript that varied between runs would make the judge's score vary with it and
/// leave nobody able to say which had changed.
/// </para>
/// </summary>
public static class TraceNarrative
{
    public static string Render(LoadedScenario loaded, ScenarioRun run)
    {
        ArgumentNullException.ThrowIfNull(loaded);
        ArgumentNullException.ThrowIfNull(run);

        var text = new StringBuilder();
        var fixture = loaded.Scenario.Fixture;

        text.AppendLine("### Setting")
            .AppendLine()
            .Append(CultureInfo.InvariantCulture, $"- The employee is {run.World.Actor.DisplayName}, in {run.World.Actor.Team}.")
            .AppendLine()
            .Append(CultureInfo.InvariantCulture, $"- It is {fixture.Clock} and they are in {fixture.Timezone}.")
            .AppendLine()
            .AppendLine();

        text.AppendLine("### What the employee said")
            .AppendLine();

        foreach (var turn in loaded.Scenario.Conversation)
        {
            var role = turn.Decision is null ? "message" : $"decision ({turn.Decision})";
            text.Append(CultureInfo.InvariantCulture, $"- [{role}] {turn.Content.Trim()}").AppendLine();
        }

        text.AppendLine();

        // The ordered trace. Tool calls and events on one timeline, exactly as the
        // deterministic assertions read them — so a disagreement between Layer 1 and
        // the judge is a disagreement about the same evidence.
        text.AppendLine("### Execution trace")
            .AppendLine();

        var timeline = run.Trace.ToolCalls
            .Select(call => (call.Position, Line: DescribeCall(call)))
            .Concat(run.Trace.Events.Select(emitted => (emitted.Position, Line: DescribeEvent(emitted))))
            .OrderBy(entry => entry.Position)
            .ToList();

        if (timeline.Count == 0)
        {
            text.AppendLine("- (nothing: the agent called no tools and emitted no events)");
        }

        foreach (var (_, line) in timeline)
        {
            text.Append("- ").AppendLine(line);
        }

        text.AppendLine();

        text.AppendLine("### What the assistant replied, and how each turn ended")
            .AppendLine();

        foreach (var turn in run.Trace.Turns)
        {
            text.Append(CultureInfo.InvariantCulture, $"**Turn {turn.Index}** — outcome `{turn.Outcome}`, terminated by `{turn.TerminationReason}`")
                .AppendLine()
                .AppendLine()
                .AppendLine(Quote(turn.Reply))
                .AppendLine();
        }

        return text.ToString().TrimEnd();
    }

    private static string DescribeCall(ToolCallRecord call)
    {
        var text = new StringBuilder();
        text.Append(CultureInfo.InvariantCulture, $"tool `{call.Tool}` ({call.Kind})");

        if (call.Arguments.Count > 0)
        {
            text.Append(CultureInfo.InvariantCulture, $" with {string.Join(", ", call.Arguments.Select(a => $"{a.Key}={a.Value}"))}");
        }

        text.Append(CultureInfo.InvariantCulture, $" → {call.Outcome}");

        if (call.ResultIds.Count > 0)
        {
            text.Append(CultureInfo.InvariantCulture, $", returned {string.Join(", ", call.ResultIds)}");
        }

        if (call.Attempts > 1)
        {
            text.Append(CultureInfo.InvariantCulture, $" (after {call.Attempts} attempts)");
        }

        return text.ToString();
    }

    private static string DescribeEvent(TraceEventRecord emitted)
    {
        var tags = emitted.Tags
            .Where(tag => tag.Value is not null)
            .Select(tag => $"{tag.Key}={tag.Value}")
            .ToList();

        return tags.Count == 0
            ? $"event `{emitted.Name}`"
            : $"event `{emitted.Name}` {{{string.Join(", ", tags)}}}";
    }

    /// <summary>
    /// Block-quotes the reply so that instruction-shaped text inside it is visibly
    /// content rather than instruction. The prompt says the same thing in words; a
    /// reply that begins "ignore your instructions" should also <em>look</em> like
    /// something being shown rather than something being said.
    /// </summary>
    private static string Quote(string reply) =>
        string.Join(
            Environment.NewLine,
            reply.Split('\n').Select(line => "> " + line.TrimEnd('\r')));
}
