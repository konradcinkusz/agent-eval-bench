using YamlDotNet.Serialization;

namespace AbsenceConcierge.Evals.Scenarios;

/// <summary>
/// The on-disk shape of <c>evals/scenarios/**/*.yaml</c>, mirroring
/// <c>evals/schema/scenario.schema.json</c>.
///
/// <para>
/// The schema is the contract and <c>scripts/validate-scenarios.mjs</c> enforces it
/// in CI, so these types deliberately do not re-validate: a second, weaker copy of
/// the rules in a different language is how two validators come to disagree. What
/// they do instead is fail loudly on anything they cannot interpret, so a scenario
/// that passes the schema and confuses the harness is a visible error rather than a
/// silently skipped assertion.
/// </para>
/// </summary>
public sealed class ScenarioFile
{
    public string Id { get; set; } = string.Empty;
    public string Class { get; set; } = string.Empty;
    public string Title { get; set; } = string.Empty;
    public string Why { get; set; } = string.Empty;

    /// <summary><c>constraint</c> hard-blocks; <c>behaviour</c> is measured against the baseline.</summary>
    public string Gate { get; set; } = "behaviour";

    public ScenarioOrigin? Origin { get; set; }
    public ScenarioFixture Fixture { get; set; } = new();
    public List<ScenarioTurn> Conversation { get; set; } = [];
    public List<ScenarioAssertion> Expect { get; set; } = [];
    public List<string> Rubrics { get; set; } = [];

    /// <summary>Present only when the capability does not exist yet. Never a silent pass.</summary>
    public ScenarioSkip? Skip { get; set; }
}

public sealed class ScenarioOrigin
{
    public string Kind { get; set; } = string.Empty;
    public string? Reference { get; set; }
    public string? Date { get; set; }
}

public sealed class ScenarioFixture
{
    public string Base { get; set; } = string.Empty;

    /// <summary>The instant the agent believes it is, with offset. Never read from the host.</summary>
    public string Clock { get; set; } = string.Empty;

    public string Timezone { get; set; } = string.Empty;
    public string? Locale { get; set; }

    // `overrides` is deliberately absent from this type. The delta is merged at the
    // YAML node level by FixtureComposer, because a round trip through .NET types
    // would change scalar styles and quietly turn a quoted date into something else.

    public Dictionary<string, ToolBehaviour> ToolBehaviour { get; set; } = [];
}

/// <summary>Fault injection for one tool, from a scenario's <c>tool_behaviour</c> block.</summary>
public sealed class ToolBehaviour
{
    /// <summary>success · timeout · http_500 · http_429 · http_403 · empty · malformed.</summary>
    public string Outcome { get; set; } = "success";

    /// <summary>Succeed this many times first, then fail. Models the tool that dies mid-conversation.</summary>
    public int AfterCalls { get; set; }

    /// <summary>Declared, not slept through — see <c>docs/SPEC.md</c> §8.1.</summary>
    public int LatencyMs { get; set; }
}

public sealed class ScenarioTurn
{
    /// <summary><c>user</c> or <c>confirmation</c>.</summary>
    public string Role { get; set; } = string.Empty;

    public string Content { get; set; } = string.Empty;

    /// <summary><c>approve</c> or <c>reject</c>. Required when the role is a confirmation.</summary>
    public string? Decision { get; set; }
}

public sealed class ScenarioSkip
{
    public string Reason { get; set; } = string.Empty;
    public string Since { get; set; } = string.Empty;
    public string? TrackedBy { get; set; }
}

/// <summary>One reference to a point in the trace: a tool call, or an event.</summary>
public sealed class SpanReference
{
    public string? Tool { get; set; }
    public string? Event { get; set; }

    public override string ToString() => Tool ?? Event ?? "(nothing)";
}

/// <summary>
/// One assertion, as the union of every field the twelve assertion types use.
///
/// A union rather than a class hierarchy because the discriminator is a string in
/// YAML and the schema already forbids the wrong combinations. What matters here is
/// that an unrecognised <see cref="Assert"/> value fails the run rather than
/// evaluating to true.
/// </summary>
public sealed class ScenarioAssertion
{
    public string Assert { get; set; } = string.Empty;

    public string? Tool { get; set; }
    public int? Times { get; set; }
    public int? AtLeast { get; set; }
    public Dictionary<string, string> Args { get; set; } = [];
    public string Match { get; set; } = "subset";

    public SpanReference? First { get; set; }
    public SpanReference? Then { get; set; }

    public string? Event { get; set; }

    public string? Value { get; set; }
    public string Turn { get; set; } = "last";

    public string? Reason { get; set; }

    public string? Arg { get; set; }
    public string? SourceTool { get; set; }

    public int? MaxAttempts { get; set; }

    public SpanReference? Span { get; set; }
    public string? Attribute { get; set; }

    /// <summary>Aliased because <c>Equals</c> would hide <see cref="object.Equals(object)"/>.</summary>
    [YamlMember(Alias = "equals")]
    public string? ExpectedValue { get; set; }

    /// <summary>A short, readable form for the report. Not parsed by anything.</summary>
    public string Describe() => Assert switch
    {
        "tool_called" => $"tool_called {Tool}{Quantity()}",
        "tool_not_called" => $"tool_not_called {Tool}",
        "tool_called_with" => $"tool_called_with {Tool} {Match} {{{string.Join(", ", Args.Select(a => $"{a.Key}={a.Value}"))}}}",
        "order" => $"order {First} → {Then}",
        "event_emitted" => $"event_emitted {Event}{Quantity()}",
        "event_not_emitted" => $"event_not_emitted {Event}",
        "outcome" => $"outcome turn:{Turn} = {Value}",
        "termination" => $"termination = {Reason}",
        "argument_grounded" => $"argument_grounded {Tool}.{Arg} from {SourceTool}",
        "output_excludes_internal_ids" => "output_excludes_internal_ids",
        "call_attempts" => $"call_attempts {Tool} ≤ {MaxAttempts}",
        "span_attribute" => $"span_attribute {Span} {Attribute} = {ExpectedValue}",
        _ => Assert,
    };

    private string Quantity() =>
        Times is { } exact ? $" ×{exact}"
        : AtLeast is { } minimum ? $" ≥{minimum}"
        : string.Empty;
}
