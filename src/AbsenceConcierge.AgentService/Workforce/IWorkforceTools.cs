namespace AbsenceConcierge.AgentService.Workforce;

/// <summary>
/// The one internal interface the agent reaches a workforce system through.
/// Implementations: <c>MockWorkforceTools</c> (the demonstrated path, in-memory,
/// zero credentials) and, from Phase 7, an MCP-backed adapter.
///
/// Extensibility is interface plus a registration line (P10). There is no base
/// class to derive from and no framework to satisfy.
/// </summary>
public interface IWorkforceTools
{
    ValueTask<ToolResult<WorkforceUser>> GetCurrentUserAsync(CancellationToken cancellationToken = default);

    ValueTask<ToolResult<IReadOnlyList<Employee>>> FindEmployeeAsync(
        string nameQuery,
        CancellationToken cancellationToken = default);

    ValueTask<ToolResult<IReadOnlyList<LeaveType>>> ListLeaveTypesAsync(
        CancellationToken cancellationToken = default);

    ValueTask<ToolResult<IReadOnlyList<Leave>>> ListLeavesAsync(
        CancellationToken cancellationToken = default);

    ValueTask<ToolResult<TimeOffResult>> RequestTimeOffAsync(
        TimeOffRequest request,
        CancellationToken cancellationToken = default);
}

public enum WorkforceToolKind
{
    Read,
    Write,
}

/// <summary>
/// The tool catalogue — the normative read/write classification from
/// <c>docs/SPEC.md</c> §2.1, in code.
///
/// This exists as data rather than as a naming convention on purpose. "No
/// write-classified span before a confirmation event" is the central constraint of
/// this agent, and a rule like <c>name.StartsWith("create_")</c> would silently
/// classify every future tool as a read until somebody remembered to rename it —
/// code that compiles, runs, and quietly matches nothing.
/// </summary>
public static class WorkforceToolCatalog
{
    public const string GetCurrentUser = "get_current_user";
    public const string FindEmployee = "find_employee";
    public const string ListLeaveTypes = "list_leave_types";
    public const string ListLeaves = "list_leaves";
    public const string RequestTimeOff = "request_time_off";

    private static readonly Dictionary<string, WorkforceToolKind> KindByName = new(StringComparer.Ordinal)
    {
        [GetCurrentUser] = WorkforceToolKind.Read,
        [FindEmployee] = WorkforceToolKind.Read,
        [ListLeaveTypes] = WorkforceToolKind.Read,
        [ListLeaves] = WorkforceToolKind.Read,
        [RequestTimeOff] = WorkforceToolKind.Write,
    };

    private static readonly Dictionary<string, string?> PermissionByName = new(StringComparer.Ordinal)
    {
        [GetCurrentUser] = null,
        [FindEmployee] = Permissions.DirectoryRead,
        [ListLeaveTypes] = Permissions.TimeOffRead,
        [ListLeaves] = Permissions.TimeOffRead,
        [RequestTimeOff] = Permissions.TimeOffRequest,
    };

    public static IReadOnlyCollection<string> Names => KindByName.Keys;

    /// <summary>
    /// Throws for an unknown tool rather than defaulting to <see cref="WorkforceToolKind.Read"/>.
    /// A default here would make "we forgot to classify the new write" indistinguishable
    /// from "this is a read", which is the failure this catalogue exists to prevent.
    /// </summary>
    public static WorkforceToolKind KindOf(string toolName) =>
        KindByName.TryGetValue(toolName, out var kind)
            ? kind
            : throw new ArgumentOutOfRangeException(
                nameof(toolName),
                toolName,
                "Unknown tool. Every tool must be classified read or write in WorkforceToolCatalog before it can be called.");

    public static string? RequiredPermission(string toolName) =>
        PermissionByName.TryGetValue(toolName, out var permission)
            ? permission
            : throw new ArgumentOutOfRangeException(nameof(toolName), toolName, "Unknown tool.");

    public static bool IsWrite(string toolName) => KindOf(toolName) == WorkforceToolKind.Write;
}

public static class Permissions
{
    public const string DirectoryRead = "directory:read";
    public const string TimeOffRead = "timeoff:read";
    public const string TimeOffRequest = "timeoff:request";
}
