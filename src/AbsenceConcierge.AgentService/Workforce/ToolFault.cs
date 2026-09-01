namespace AbsenceConcierge.AgentService.Workforce;

/// <summary>
/// How one named tool should fail. A scenario's <c>tool_behaviour</c> block and the
/// browser suite's configuration both bind to this, so "the backend returned 500"
/// means one thing in the estate rather than one thing per test suite.
/// </summary>
public sealed class ToolFault
{
    /// <summary>success · timeout · http_500 · http_429 · http_403 · empty · malformed.</summary>
    public string Outcome { get; set; } = "success";

    /// <summary>Succeed this many times first, then fail. Models the tool that dies mid-conversation.</summary>
    public int AfterCalls { get; set; }

    /// <summary>Declared, not slept through — see <c>docs/SPEC.md</c> §8.1.</summary>
    public int LatencyMs { get; set; }
}
