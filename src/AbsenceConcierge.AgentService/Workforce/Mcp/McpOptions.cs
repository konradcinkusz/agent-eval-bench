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
    /// A bearer token for the server.
    ///
    /// Held as configuration so it arrives from user secrets locally and from an
    /// environment secret in CI, and so it is never read out of the environment by
    /// application code that thought it was reading configuration.
    /// </summary>
    public string? AccessToken { get; set; }

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
