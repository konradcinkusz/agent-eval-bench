using AbsenceConcierge.AgentService.Workforce.Mcp;

namespace AbsenceConcierge.AgentService.Tests;

/// <summary>
/// A Model Context Protocol server, in about forty lines.
///
/// <para>
/// This is the reason <see cref="IMcpToolSession"/> exists. The MCP adapter's
/// interesting behaviour — the confirmation token redeemed before the write, the
/// only_for_self filter applied to what comes back, the difference between "this
/// definitely failed" and "this may have happened" — is all above the SDK, so all of
/// it is testable in milliseconds against this. A design that could only be exercised
/// against a live server would be a design this repository never exercises at all.
/// </para>
/// <para>
/// An unregistered tool <b>throws</b> rather than returning an error reply. A test
/// that forgot to arrange a call would otherwise get a plausible failure and could
/// assert its way to green against the wrong cause.
/// </para>
/// </summary>
public sealed class FakeMcpToolSession : IMcpToolSession
{
    private readonly Dictionary<string, Func<IReadOnlyDictionary<string, object?>, McpToolReply>> _handlers =
        new(StringComparer.Ordinal);

    public List<(string Tool, IReadOnlyDictionary<string, object?> Arguments)> Calls { get; } = [];

    public bool Disposed { get; private set; }

    public FakeMcpToolSession Answering(string tool, McpToolReply reply)
    {
        _handlers[tool] = _ => reply;
        return this;
    }

    public FakeMcpToolSession AnsweringJson(string tool, string json) =>
        Answering(tool, McpToolReply.Ok(json));

    /// <summary>Answers differently each time, for the "and then it failed" cases.</summary>
    public FakeMcpToolSession AnsweringInTurn(string tool, params McpToolReply[] replies)
    {
        var index = 0;
        _handlers[tool] = _ => replies[Math.Min(index++, replies.Length - 1)];
        return this;
    }

    public int CallsTo(string tool) => Calls.Count(call => string.Equals(call.Tool, tool, StringComparison.Ordinal));

    public IReadOnlyDictionary<string, object?> LastArgumentsTo(string tool) =>
        Calls.Last(call => string.Equals(call.Tool, tool, StringComparison.Ordinal)).Arguments;

    public ValueTask<McpToolReply> CallAsync(
        string tool,
        IReadOnlyDictionary<string, object?> arguments,
        CancellationToken cancellationToken = default)
    {
        Calls.Add((tool, arguments));

        if (!_handlers.TryGetValue(tool, out var handler))
        {
            throw new InvalidOperationException(
                $"No reply was arranged for '{tool}'. Arranged: [{string.Join(", ", _handlers.Keys)}].");
        }

        return ValueTask.FromResult(handler(arguments));
    }

    public ValueTask DisposeAsync()
    {
        Disposed = true;
        return ValueTask.CompletedTask;
    }
}
