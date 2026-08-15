using System.Diagnostics;
using System.Net;
using System.Text;
using ModelContextProtocol;
using ModelContextProtocol.Authentication;
using ModelContextProtocol.Client;
using ModelContextProtocol.Protocol;

namespace AbsenceConcierge.AgentService.Workforce.Mcp;

/// <summary>
/// The Model Context Protocol SDK, and the only file in this repository that names it.
///
/// <para>
/// It does one job: turn a tool name and a bag of arguments into an
/// <see cref="McpToolReply"/>, and turn every way that can go wrong into a value
/// rather than an exception. Nothing in here knows what a leave type is.
/// </para>
/// <para>
/// <b>The classification in the catch blocks is the content of this file.</b> The
/// agent must be able to tell "this definitely did not happen" from "this may have
/// happened" (SPEC §7.2), and for a write those are different sentences to a human:
/// one invites a retry and the other forbids it. A DNS failure or a refused
/// connection never reached the server. A timeout, a cancelled read mid-response, or
/// a socket that died after the request went out did reach it, or might have. Only
/// the first group is safe to call a definite failure, so only the first group is
/// mapped that way.
/// </para>
/// </summary>
public sealed class McpClientSession : IMcpToolSession
{
    private readonly IClientTransport _transport;
    private readonly TimeSpan _timeout;
    private readonly SemaphoreSlim _connecting = new(1, 1);

    private McpClient? _client;
    private bool _disposed;

    public McpClientSession(McpOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);

        if (string.IsNullOrWhiteSpace(options.ServerUrl))
        {
            // Reaching here means the caller decided MCP mode was available without
            // checking. The degradation decision belongs at composition (P8), so this
            // is a programming error rather than a configuration one.
            throw new InvalidOperationException(
                $"{McpOptions.SectionName}:ServerUrl is required before a session can be opened.");
        }

        _timeout = TimeSpan.FromSeconds(Math.Clamp(options.TimeoutSeconds, 1, 300));

        var headers = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        if (!string.IsNullOrWhiteSpace(options.AccessToken))
        {
            headers["Authorization"] = $"Bearer {options.AccessToken}";
        }

        _transport = new HttpClientTransport(new HttpClientTransportOptions
        {
            Endpoint = new Uri(options.ServerUrl, UriKind.Absolute),
            TransportMode = HttpTransportMode.StreamableHttp,
            ConnectionTimeout = _timeout,
            AdditionalHeaders = headers,

            // OAuth 2.0 with dynamic client registration, when the deployment asked
            // for it (D-11). The flow — discovery, registration, PKCE, token
            // exchange, refresh — is the SDK's own implementation; what this file
            // contributes is configuration and the loopback listener a native app
            // needs for the redirect. A bearer token and OAuth can coexist in
            // configuration; when both are present the SDK's provider supersedes
            // the static header on the requests it authorises.
            OAuth = options.OAuth.Enabled ? BuildOAuth(options.OAuth) : null,
        });
    }

    /// <summary>
    /// The SDK's OAuth options, from this repository's configuration vocabulary.
    ///
    /// <para>
    /// With no <c>ClientId</c> the SDK performs RFC 7591 dynamic client
    /// registration under the configured display name — which is the mode a server
    /// like Factorial's expects. Scopes are a fallback by SDK contract: the
    /// server's advertised scopes win when it advertises any, which is the right
    /// direction for an anti-corruption boundary (P11).
    /// </para>
    /// </summary>
    private static ClientOAuthOptions BuildOAuth(McpOAuthOptions oauth) => new()
    {
        RedirectUri = new Uri(oauth.RedirectUri, UriKind.Absolute),
        ClientId = string.IsNullOrWhiteSpace(oauth.ClientId) ? null : oauth.ClientId,
        ClientSecret = string.IsNullOrWhiteSpace(oauth.ClientSecret) ? null : oauth.ClientSecret,
        Scopes = oauth.Scopes.Count == 0 ? null : [.. oauth.Scopes],
        DynamicClientRegistration = new DynamicClientRegistrationOptions
        {
            ClientName = oauth.ClientName,
        },
        AuthorizationCallbackHandler = (context, cancellationToken) =>
            AuthoriseAtLoopbackAsync(context, cancellationToken),
    };

    /// <summary>
    /// The human half of the flow: print the authorization URL, try to open a
    /// browser, and catch the redirect on the loopback listener. Response-bound —
    /// code, state and issuer all travel back — so the SDK's state and RFC 9207
    /// checks run rather than being waived.
    ///
    /// <para>
    /// This is what makes OAuth mode development-only in mechanics and not merely
    /// in documentation: a headless deployment has nobody at the URL and nothing
    /// listening on loopback, so the flow cannot complete there even if someone
    /// sets the flag (ADR-0002's direction, applied to an interactive credential).
    /// </para>
    /// </summary>
    private static async Task<AuthorizationResult?> AuthoriseAtLoopbackAsync(
        AuthorizationCallbackContext context,
        CancellationToken cancellationToken)
    {
        using var listener = new HttpListener();

        // HttpListener requires the trailing slash; a configured redirect without
        // one must not turn into a cryptic ArgumentException at authorise time.
        var prefix = context.RedirectUri.GetLeftPart(UriPartial.Path);
        listener.Prefixes.Add(prefix.EndsWith('/') ? prefix : prefix + "/");
        listener.Start();

        Console.WriteLine();
        Console.WriteLine("MCP OAuth: open this URL to authorise the agent:");
        Console.WriteLine($"  {context.AuthorizationUri}");
        Console.WriteLine();

        TryOpenBrowser(context.AuthorizationUri.ToString());

        var pending = listener.GetContextAsync();
        var callback = await pending.WaitAsync(cancellationToken).ConfigureAwait(false);

        var query = System.Web.HttpUtility.ParseQueryString(callback.Request.Url?.Query ?? string.Empty);

        var body = Encoding.UTF8.GetBytes(
            "<!doctype html><meta charset=\"utf-8\"><title>Authorised</title>"
            + "<p>Authorisation received. You can close this tab and return to the terminal.</p>");

        callback.Response.ContentType = "text/html; charset=utf-8";
        callback.Response.ContentLength64 = body.Length;
        await callback.Response.OutputStream.WriteAsync(body, cancellationToken).ConfigureAwait(false);
        callback.Response.Close();

        return new AuthorizationResult
        {
            Code = query["code"],
            State = query["state"],
            Iss = query["iss"],
        };
    }

    /// <summary>
    /// Best effort, never load-bearing: the URL is already on the console, and a
    /// machine with no browser launcher is a machine whose operator can click it
    /// somewhere else.
    /// </summary>
    private static void TryOpenBrowser(string url)
    {
        try
        {
            using var _ = Process.Start(new ProcessStartInfo(url) { UseShellExecute = true });
        }
#pragma warning disable CA1031 // Opening a browser is a convenience; any failure degrades to the printed URL.
        catch (Exception)
        {
            // The printed URL is the contract; the browser was a courtesy.
        }
#pragma warning restore CA1031
    }

    public async ValueTask<McpToolReply> CallAsync(
        string tool,
        IReadOnlyDictionary<string, object?> arguments,
        CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        ArgumentException.ThrowIfNullOrWhiteSpace(tool);
        ArgumentNullException.ThrowIfNull(arguments);

        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        deadline.CancelAfter(_timeout);

        try
        {
            var client = await ConnectAsync(deadline.Token).ConfigureAwait(false);

            var result = await client
                .CallToolAsync(tool, arguments, cancellationToken: deadline.Token)
                .ConfigureAwait(false);

            // A tool that failed still answers with content — that is the protocol's
            // whole design, and it is why `IsError` is not an exception here either.
            var text = result.Content is { } blocks
                ? blocks.OfType<TextContentBlock>().FirstOrDefault()?.Text
                : null;

            return result.IsError is true ? McpToolReply.ToolError(text) : McpToolReply.Ok(text);
        }
        catch (McpException ex)
        {
            // A JSON-RPC error response. The server received the call, parsed it, and
            // answered with a refusal — an unknown tool, a malformed argument. Nothing
            // ran, and that is a definite answer.
            return McpToolReply.ToolError(ex.Message);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            // Our own deadline, not the caller's. The request left this process and
            // never came back, which is the case McpToolReply.Unknown exists for.
            return McpToolReply.Unknown($"'{tool}' did not answer within {_timeout.TotalSeconds:0}s.");
        }
        catch (HttpRequestException ex) when (NeverReachedTheServer(ex))
        {
            return McpToolReply.Transport($"The server could not be reached: {ex.HttpRequestError}.");
        }
        catch (HttpRequestException ex)
        {
            // Reached the server, then something went wrong. Whether the tool ran is
            // exactly what we do not know.
            return McpToolReply.Unknown($"The call to '{tool}' failed after the request was sent: {ex.HttpRequestError}.");
        }
        catch (IOException ex)
        {
            // A stream that died mid-response. Same reasoning as above.
            return McpToolReply.Unknown($"The response to '{tool}' was cut short: {ex.GetType().Name}.");
        }
    }

    public async ValueTask DisposeAsync()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;

        if (_client is { } client)
        {
            await client.DisposeAsync().ConfigureAwait(false);
        }

        // Typed as the interface so this reads as a question about the transport in
        // hand rather than an assertion about the SDK's current class hierarchy.
        if (_transport is IAsyncDisposable transport)
        {
            await transport.DisposeAsync().ConfigureAwait(false);
        }

        _connecting.Dispose();
    }

    /// <summary>
    /// Failures that happened before anything left this machine. Anything not on this
    /// list is treated as "we do not know", which is the conservative direction: the
    /// cost of a wrong "it definitely failed" on a write is a human filing the same
    /// leave twice.
    /// </summary>
    private static bool NeverReachedTheServer(HttpRequestException exception) =>
        exception.HttpRequestError
            is HttpRequestError.ConnectionError
            or HttpRequestError.NameResolutionError
            or HttpRequestError.SecureConnectionError
            or HttpRequestError.ProxyTunnelError;

    /// <summary>
    /// Opens the session on first use, never at startup.
    ///
    /// <para>
    /// Connecting in the constructor would make an unreachable server a failed
    /// service start, and P8 is explicit that an optional dependency being absent
    /// degrades the feature rather than the process. It also means the initialize
    /// handshake's failure is classified by the same catch blocks as a tool call's,
    /// in one place.
    /// </para>
    /// </summary>
    private async ValueTask<McpClient> ConnectAsync(CancellationToken cancellationToken)
    {
        if (_client is { } connected)
        {
            return connected;
        }

        await _connecting.WaitAsync(cancellationToken).ConfigureAwait(false);

        try
        {
            _client ??= await McpClient
                .CreateAsync(_transport, cancellationToken: cancellationToken)
                .ConfigureAwait(false);

            return _client;
        }
        finally
        {
            _connecting.Release();
        }
    }
}
