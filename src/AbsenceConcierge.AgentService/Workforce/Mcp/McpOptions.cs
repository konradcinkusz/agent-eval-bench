namespace AbsenceConcierge.AgentService.Workforce.Mcp;

/// <summary>
/// How to reach a Model Context Protocol server, and what it calls the five tools
/// this agent needs.
///
/// <para>
/// <b>Nothing here ships with a value that reaches a server.</b> An unset
/// <see cref="ServerUrl"/> or <see cref="AccessToken"/> means MCP mode is
/// unavailable and the service runs on the mock — it does not mean the service
/// fails to start, and it does not mean a half-configured client tries anyway
/// (P8). The public deployment carries none of these settings at all.
/// </para>
/// </summary>
public sealed class McpOptions
{
    public const string SectionName = "WorkforceTools:Mcp";

    /// <summary>The Streamable HTTP endpoint. Never committed.</summary>
    public string? ServerUrl { get; set; }

    /// <summary>
    /// A bearer token for the server, obtained out of band. One of two ways in:
    /// this, or the OAuth flow below. Both absent means MCP mode is unavailable.
    ///
    /// Held as configuration so it arrives from user secrets locally and from an
    /// environment secret in CI, and so it is never read out of the environment by
    /// application code that thought it was reading configuration.
    /// </summary>
    public string? AccessToken { get; set; }

    /// <summary>
    /// OAuth 2.0 with dynamic client registration — the other way in, and the one
    /// a server like Factorial's actually speaks (D-11).
    ///
    /// <para>
    /// <b>Development-only by intent and by mechanics.</b> The flow needs a human
    /// at a browser and a loopback listener for the redirect; a headless
    /// deployment cannot complete it, and the public demo carries none of these
    /// settings so the branch stays unreachable there (ADR-0005). The flow itself
    /// — discovery, registration, PKCE, token exchange, refresh — is the SDK's,
    /// selected here rather than reimplemented: rewriting a tested OAuth stack
    /// from documentation is the exact failure D-11 was recorded to avoid.
    /// </para>
    /// </summary>
    public McpOAuthOptions OAuth { get; set; } = new();

    public int TimeoutSeconds { get; set; } = 30;

    /// <summary>
    /// What the remote server calls each of this agent's tools.
    ///
    /// <para>
    /// Configurable because this is the anti-corruption boundary and a foreign
    /// system's vocabulary is exactly what a boundary exists to absorb (P11). The
    /// defaults are this repository's own names; a real server will differ, and
    /// changing four strings in configuration is the whole cost of that.
    /// </para>
    /// </summary>
    public McpToolNames ToolNames { get; set; } = new();

    /// <summary>
    /// The server's scope strings, mapped to this repository's permission vocabulary
    /// (<see cref="Permissions"/>).
    ///
    /// <para>
    /// Two agent steps refuse before calling a tool when the actor lacks a permission,
    /// so this is what lets a denied path stay a denied path in MCP mode. Left empty —
    /// the default, because no server's scope names are knowable in advance — the agent
    /// stops pre-empting and the server does all the refusing. That degradation is
    /// logged once per process, not silently assumed.
    /// </para>
    /// </summary>
    public IDictionary<string, string> PermissionScopes { get; } =
        new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// Substrings that identify a permission refusal in a server's error text.
    ///
    /// <para>
    /// Empty by default, and that emptiness is the decision: guessing which of a
    /// foreign system's error messages mean "not allowed" is string-matching prose
    /// that the next release rewords. A deployment that knows its server can say so
    /// here; without it, a refused call is reported as a refusal rather than
    /// mis-reported as a permissions problem.
    /// </para>
    /// </summary>
    public IList<string> PermissionDeniedMarkers { get; } = [];
}

/// <summary>
/// How the OAuth flow identifies and receives its answers. Nothing here is a
/// secret except <see cref="ClientSecret"/>, which only exists for a server that
/// pre-registered this client — with dynamic registration the client has no
/// secret at all and PKCE carries the proof.
/// </summary>
public sealed class McpOAuthOptions
{
    /// <summary>
    /// Off by default. An explicit switch rather than inference from the other
    /// fields, so that a half-filled section reads as "misconfigured" in a log
    /// line and not as "bearer mode".
    /// </summary>
    public bool Enabled { get; set; }

    /// <summary>Shown to the human on the server's consent screen during dynamic registration.</summary>
    public string ClientName { get; set; } = "absence-concierge (agent-eval-bench)";

    /// <summary>
    /// Where the authorization response lands. Loopback by design: the listener
    /// binds this exact address for the seconds the flow is in flight, which is
    /// the standard native-app pattern (RFC 8252) and one more reason this mode
    /// cannot run headless.
    /// </summary>
    public string RedirectUri { get; set; } = "http://127.0.0.1:53682/callback/";

    /// <summary>
    /// Fallback scopes, used only when the server advertises none. The server's
    /// own advertisement wins — that is the SDK's scope-selection order, and it is
    /// the right one: a foreign system's scope names are exactly what this
    /// boundary exists to absorb (P11).
    /// </summary>
    public IList<string> Scopes { get; } = [];

    /// <summary>Set only for a pre-registered client. Absent, the client registers dynamically.</summary>
    public string? ClientId { get; set; }

    /// <summary>Never committed. Meaningful only alongside <see cref="ClientId"/>.</summary>
    public string? ClientSecret { get; set; }
}

public sealed class McpToolNames
{
    public string GetCurrentUser { get; set; } = WorkforceToolCatalog.GetCurrentUser;
    public string FindEmployee { get; set; } = WorkforceToolCatalog.FindEmployee;
    public string ListLeaveTypes { get; set; } = WorkforceToolCatalog.ListLeaveTypes;
    public string ListLeaves { get; set; } = WorkforceToolCatalog.ListLeaves;
    public string RequestTimeOff { get; set; } = WorkforceToolCatalog.RequestTimeOff;

    public string For(string catalogueName) => catalogueName switch
    {
        WorkforceToolCatalog.GetCurrentUser => GetCurrentUser,
        WorkforceToolCatalog.FindEmployee => FindEmployee,
        WorkforceToolCatalog.ListLeaveTypes => ListLeaveTypes,
        WorkforceToolCatalog.ListLeaves => ListLeaves,
        WorkforceToolCatalog.RequestTimeOff => RequestTimeOff,

        // Throws rather than passing the unknown name through. A tool this agent
        // does not have in its catalogue is a tool nobody classified read or write,
        // and the classification is what C-1 is derived from.
        _ => throw new ArgumentOutOfRangeException(nameof(catalogueName), catalogueName, "Not a catalogue tool."),
    };
}
