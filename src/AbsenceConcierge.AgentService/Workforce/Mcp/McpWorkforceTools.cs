using System.Globalization;
using AbsenceConcierge.AgentService.Workforce.Confirmation;

namespace AbsenceConcierge.AgentService.Workforce.Mcp;

/// <summary>
/// <see cref="IWorkforceTools"/>, served by a Model Context Protocol server.
///
/// <para>
/// The agent above this class cannot tell it apart from the mock, which is the point:
/// every eval scenario, every trace assertion and every constraint in SPEC §4 is
/// written against the interface, so the integration inherits them instead of needing
/// its own parallel suite. What it does <em>not</em> inherit is the mock's authority
/// over the world — in this mode the server owns the data and the permissions, and
/// this class owns the two things that must hold regardless of what the server does:
/// </para>
/// <list type="number">
///   <item><b>The confirmation gate.</b> The token is redeemed here, before the
///     remote write, against a draft whose employee id came from the server rather
///     than from the arguments. SPEC §2.1.1 claims the gate is enforced at the tool
///     boundary "in mock mode and in MCP mode alike"; this is what makes that
///     sentence true rather than aspirational.</item>
///   <item><b>only_for_self.</b> The actor's id is sent as a filter and the reply is
///     filtered again on the way back. A server that ignores the filter does not get
///     to hand this agent a colleague's bookings.</item>
/// </list>
/// <para>
/// <b>This has never run against a live server.</b> It is written to be read and to
/// be exercised by a fake session; the first real run will find something, and the
/// failure messages are written for that run. See <c>docs/DEVIATIONS.md</c> D-10.
/// </para>
/// </summary>
public sealed class McpWorkforceTools(
    IMcpToolSession session,
    McpOptions options,
    IConfirmationTokenStore confirmationTokens,
    ILogger<McpWorkforceTools> logger) : IWorkforceTools
{
    private static readonly Dictionary<string, object?> NoArguments = new(StringComparer.Ordinal);

    private WorkforceUser? _actor;
    private int _permissionNoticeLogged;

    public async ValueTask<ToolResult<WorkforceUser>> GetCurrentUserAsync(
        CancellationToken cancellationToken = default)
    {
        var result = await CallAsync(
            WorkforceToolCatalog.GetCurrentUser,
            NoArguments,
            text => WithPermissions(McpPayloads.User(text)),
            cancellationToken).ConfigureAwait(false);

        if (result.Value is { } user)
        {
            _actor = user;
        }

        return result;
    }

    public ValueTask<ToolResult<IReadOnlyList<Employee>>> FindEmployeeAsync(
        string nameQuery,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(nameQuery))
        {
            return ValueTask.FromResult(
                ToolResult<IReadOnlyList<Employee>>.Rejected("A name to search for is required."));
        }

        return CallAsync(
            WorkforceToolCatalog.FindEmployee,

            // The argument names are this repository's, like the tool names above them,
            // and a real server will differ on both. Tool names are configurable
            // because they are the part a reader can see is foreign; argument names
            // are the next thing this adapter needs and do not have a knob yet.
            new Dictionary<string, object?>(StringComparer.Ordinal) { ["name_query"] = nameQuery },
            McpPayloads.Employees,
            cancellationToken);
    }

    public ValueTask<ToolResult<IReadOnlyList<LeaveType>>> ListLeaveTypesAsync(
        CancellationToken cancellationToken = default) =>
        CallAsync(
            WorkforceToolCatalog.ListLeaveTypes,
            NoArguments,
            McpPayloads.LeaveTypes,
            cancellationToken);

    public async ValueTask<ToolResult<IReadOnlyList<Leave>>> ListLeavesAsync(
        CancellationToken cancellationToken = default)
    {
        var actor = await ResolveActorAsync(cancellationToken).ConfigureAwait(false);

        if (actor is null)
        {
            return ToolResult<IReadOnlyList<Leave>>.Failed(
                "The current user could not be established, so no bookings were read.");
        }

        var result = await CallAsync(
            WorkforceToolCatalog.ListLeaves,
            new Dictionary<string, object?>(StringComparer.Ordinal) { ["employee_id"] = actor.EmployeeId },
            McpPayloads.Leaves,
            cancellationToken).ConfigureAwait(false);

        if (result.Value is not { } leaves)
        {
            return result;
        }

        // Asked for, and then checked. The filter above is a courtesy to the server's
        // query planner; this is the part that holds when the server ignores it.
        IReadOnlyList<Leave> mine = [.. leaves.Where(leave =>
            string.Equals(leave.EmployeeId, actor.EmployeeId, StringComparison.Ordinal))];

        if (mine.Count != leaves.Count)
        {
            logger.LogWarning(
                "list_leaves returned {Returned} bookings of which {Kept} belong to the current user; "
                + "the rest were discarded. The server did not honour the employee filter.",
                leaves.Count,
                mine.Count);
        }

        return ToolResult<IReadOnlyList<Leave>>.Ok(mine);
    }

    public async ValueTask<ToolResult<TimeOffResult>> RequestTimeOffAsync(
        TimeOffRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        // The employee id has to come from the server. It is half of what the token is
        // bound to, and taking it from the request would mean an injected instruction
        // that changed whose leave this is would also change what the approval covered.
        var actor = await ResolveActorAsync(cancellationToken).ConfigureAwait(false);

        if (actor is null)
        {
            return ToolResult<TimeOffResult>.Failed(
                "The current user could not be established, so no write was attempted.");
        }

        var draft = new ConfirmationDraft(
            actor.EmployeeId,
            request.LeaveTypeId,
            request.StartDate,
            request.EndDate);

        // Checked before anything is sent, and before the arguments are validated, for
        // the same reason the mock checks it first: an unconfirmed write must be
        // refused for being unconfirmed.
        if (!confirmationTokens.TryRedeem(request.ConfirmationToken, draft))
        {
            return ToolResult<TimeOffResult>.ConfirmationRequired(
                "This request has not been confirmed by the employee, or the confirmation "
                + "does not match what is being submitted.");
        }

        // The token is now spent, and it is spent whatever the server answers. That is
        // C-6 holding through a failure: an indeterminate write must not be retried,
        // and the cheapest way to guarantee it is to have nothing left to retry with.
        return await CallAsync(
            WorkforceToolCatalog.RequestTimeOff,
            new Dictionary<string, object?>(StringComparer.Ordinal)
            {
                ["leave_type_id"] = request.LeaveTypeId,
                ["start_date"] = request.StartDate.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
                ["end_date"] = request.EndDate.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
            },
            McpPayloads.TimeOff,
            cancellationToken).ConfigureAwait(false);
    }

    private async ValueTask<ToolResult<T>> CallAsync<T>(
        string catalogueTool,
        IReadOnlyDictionary<string, object?> arguments,
        Func<string?, T> read,
        CancellationToken cancellationToken)
    {
        var remoteName = options.ToolNames.For(catalogueTool);

        var reply = await session.CallAsync(remoteName, arguments, cancellationToken).ConfigureAwait(false);

        if (reply.IsError)
        {
            return Failure<T>(catalogueTool, remoteName, reply);
        }

        try
        {
            return ToolResult<T>.Ok(read(reply.Text));
        }
        catch (McpPayloadException ex)
        {
            logger.LogError(ex, "The reply from MCP tool {Tool} could not be read.", remoteName);

            // The message names keys, never values, and it is the only remote-shaped
            // detail this class puts anywhere near a span (see Failure below).
            return ToolResult<T>.Failed(ex.Message);
        }
    }

    /// <summary>
    /// Turns a failed reply into the outcome the agent reasons about.
    ///
    /// <para>
    /// The server's own error text never becomes part of the result. It is remote
    /// free text: it may name a person, and <see cref="InstrumentedWorkforceTools"/>
    /// puts a result's message on the span as its status description, where it would
    /// be exported. It goes to the log, which is the place that is allowed to be
    /// verbose and is not shipped to a collector by default.
    /// </para>
    /// <para>
    /// There is deliberately no attempt to read "permission denied" out of that text
    /// by pattern. String-matching a foreign system's prose is precisely the coupling
    /// this boundary exists to prevent, and it breaks on the release that reworded the
    /// message. A deployment that knows its server's wording can name the markers in
    /// configuration; the default is to make no claim.
    /// </para>
    /// </summary>
    private ToolResult<T> Failure<T>(string catalogueTool, string remoteName, McpToolReply reply)
    {
        var isWrite = WorkforceToolCatalog.IsWrite(catalogueTool);

        if (reply.Message is { } transport)
        {
            logger.LogWarning("MCP tool {Tool} failed below the protocol: {Detail}", remoteName, transport);

            // "May have happened" is only ever said about a write. A read that may or
            // may not have run is a read that did not produce data, and the agent's
            // degradation path already handles that.
            return reply.Indeterminate && isWrite
                ? ToolResult<T>.Indeterminate("The request may or may not have reached the workforce system.")
                : ToolResult<T>.Failed("The workforce system could not be reached.");
        }

        logger.LogWarning("MCP tool {Tool} reported an error: {Detail}", remoteName, reply.Text);

        if (IsPermissionDenial(reply.Text))
        {
            return ToolResult<T>.Denied("You do not have permission to do that.");
        }

        // The server answered, so nothing is in flight. A refused write definitely did
        // not happen, which is a different sentence to a human from a failed one.
        return isWrite
            ? ToolResult<T>.Rejected("The workforce system declined the request.")
            : ToolResult<T>.Failed("The workforce system declined the request.");
    }

    private bool IsPermissionDenial(string? errorText) =>
        errorText is { Length: > 0 }
        && options.PermissionDeniedMarkers.Count > 0
        && options.PermissionDeniedMarkers.Any(marker =>
            errorText.Contains(marker, StringComparison.OrdinalIgnoreCase));

    /// <summary>
    /// Translates the server's scopes into this repository's permission vocabulary,
    /// which two agent steps read directly to refuse before calling.
    ///
    /// <para>
    /// With no translation table configured, the actor is given the full set — not
    /// because the actor has it, but because the agent must not invent a refusal on
    /// behalf of a server whose vocabulary it cannot read. The server stays the
    /// authority and refuses what it refuses; the agent simply stops pre-empting it.
    /// The alternative — an empty permission set — would make every request fail with
    /// a confident "you are not allowed", which is a wrong answer delivered in the
    /// tone of a right one.
    /// </para>
    /// </summary>
    private WorkforceUser WithPermissions(WorkforceUser user)
    {
        if (options.PermissionScopes.Count > 0)
        {
            IReadOnlyList<string> translated = [.. user.Permissions
                .Select(scope => options.PermissionScopes.TryGetValue(scope, out var mapped) ? mapped : null)
                .OfType<string>()
                .Distinct(StringComparer.Ordinal)];

            return user with { Permissions = translated };
        }

        if (Interlocked.Exchange(ref _permissionNoticeLogged, 1) == 0)
        {
            logger.LogWarning(
                "{Section}:PermissionScopes is empty, so the agent will not refuse in advance on the "
                + "grounds of permissions; the server remains the authority and its refusals are "
                + "reported as they arrive.",
                McpOptions.SectionName);
        }

        return user with { Permissions = WorkforceToolCatalog.AllPermissions };
    }

    /// <summary>
    /// The current user, from cache or from the server.
    ///
    /// <para>
    /// Resolved on demand rather than assumed, so that a write cannot be reached by a
    /// path that skipped <c>get_current_user</c>. The agent's first step always calls
    /// it, which makes this a no-op in practice — and the whole point of a boundary is
    /// that it holds when the thing above it changes.
    /// </para>
    /// </summary>
    private async ValueTask<WorkforceUser?> ResolveActorAsync(CancellationToken cancellationToken)
    {
        if (_actor is { } known)
        {
            return known;
        }

        var result = await GetCurrentUserAsync(cancellationToken).ConfigureAwait(false);

        return result.IsSuccess ? result.Value : null;
    }
}
