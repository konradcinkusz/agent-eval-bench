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
/// It is also P11 taken literally: <b>exactly one file imports the SDK</b>.
/// <code>grep -rl '^using ModelContextProtocol' src/</code> returns
/// <c>McpClientSession.cs</c> and nothing else.
/// </para>
/// <para>
/// The claim is worded that way because the looser one is false and a reader can
/// check in a second: a bare <c>grep -r ModelContextProtocol src/</c> also matches
/// the package reference in the .csproj, this very comment, and every build
/// artefact under obj/ and bin/. The seam is about who may name the vendor's
/// types, and a `using` directive is where that is decided.
/// </para>
/// </summary>
public interface IMcpToolSession : IAsyncDisposable
{
    ValueTask<McpToolReply> CallAsync(
        string tool,
        IReadOnlyDictionary<string, object?> arguments,
        CancellationToken cancellationToken = default);
}
