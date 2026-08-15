namespace AbsenceConcierge.AgentService.Workforce.Mcp;

/// <param name="IsError">The server reported the tool itself failed. Distinct from a transport failure.</param>
/// <param name="Text">The first text content block, which is where a tool's payload arrives.</param>
/// <param name="Message">Why, when something went wrong at the transport rather than in the tool.</param>
/// <param name="Indeterminate">
/// The call may or may not have been carried out. A timeout on a write is the case
/// this exists for, and SPEC §7.2 makes it a different answer from a failure.
/// </param>
public sealed record McpToolReply(bool IsError, string? Text, string? Message, bool Indeterminate = false)
{
    public static McpToolReply Ok(string? text) => new(false, text, null);

    public static McpToolReply ToolError(string? text) => new(true, text, null);

    public static McpToolReply Transport(string message) => new(true, null, message);

    public static McpToolReply Unknown(string message) => new(true, null, message, Indeterminate: true);
}

/// <summary>
/// One remote tool call, with no vendor type in the signature.
///
/// <para>
/// This is the seam the Model Context Protocol SDK lives behind. Everything above
/// it — the mapping into domain records, the confirmation-token check, the failure
/// classification — is ordinary code that a fake session exercises in
/// milliseconds, which matters more than usual here: this repository ships without
/// a server to point at, and a design that could only be tested against one would
/// be a design nothing in this repository ever tests.
/// </para>
/// <para>
/// It is also P11 taken literally. `grep -r ModelContextProtocol src/` returns
/// exactly one file.
/// </para>
/// </summary>
public interface IMcpToolSession : IAsyncDisposable
{
    ValueTask<McpToolReply> CallAsync(
        string tool,
        IReadOnlyDictionary<string, object?> arguments,
        CancellationToken cancellationToken = default);
}
