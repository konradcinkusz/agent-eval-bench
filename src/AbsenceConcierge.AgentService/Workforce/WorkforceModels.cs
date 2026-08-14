namespace AbsenceConcierge.AgentService.Workforce;

/// <summary>
/// The internal workforce model. Nothing downstream of this file knows which
/// system the data came from (P11 — anti-corruption at the edge): the Model
/// Context Protocol dialect and the in-memory mock are both normalised into
/// these types at the boundary, once.
///
/// The test of whether that boundary holds is mechanical — no vendor name
/// appears in this namespace, in the agent loop, or in any eval scenario.
/// Adding a second workforce backend should cost one adapter file.
/// </summary>
public sealed record WorkforceUser(
    string EmployeeId,
    string DisplayName,
    string Team,
    IReadOnlyList<string> Permissions);

public sealed record Employee(
    string EmployeeId,
    string DisplayName,
    string Team);

public sealed record LeaveType(
    string Id,
    string Name,
    bool RequiresApproval,
    bool CountsAgainstBalance,
    int MaxConsecutiveDays,
    bool AllowsHalfDays,
    int? RequiresAttachmentAfterDays);

public sealed record Leave(
    string Id,
    string EmployeeId,
    string LeaveTypeId,
    DateOnly StartDate,
    DateOnly EndDate,
    string Status);

public sealed record CompanyHoliday(DateOnly Date, string Name);

/// <summary>
/// A time-off request. <see cref="ConfirmationToken"/> is not decoration: the tool
/// layer refuses any write whose token is missing, unknown, or bound to a different
/// draft. See <c>docs/SPEC.md</c> §2.1.1 — without it the confirmation gate would
/// exist only inside the agent, and "the agent's good behaviour is UX; the service
/// boundary is security" would be a claim this repository could not back.
/// </summary>
public sealed record TimeOffRequest(
    string LeaveTypeId,
    DateOnly StartDate,
    DateOnly EndDate,
    string ConfirmationToken);

public sealed record TimeOffResult(
    string RequestId,
    string Status,
    DateOnly StartDate,
    DateOnly EndDate);

/// <summary>
/// Why a tool call did not succeed. Modelled as a value rather than an exception
/// because degradation is a specified behaviour here, not an error path: the agent
/// must be able to tell "you may not do this" from "this did not work right now"
/// from "this may or may not have happened", and report each differently
/// (<c>docs/SPEC.md</c> §7.2).
/// </summary>
public enum ToolOutcome
{
    Success,

    /// <summary>The actor's permission fixture does not allow this call.</summary>
    PermissionDenied,

    /// <summary>The write carried no valid confirmation token. The gate, enforced at the boundary.</summary>
    ConfirmationRequired,

    /// <summary>The request is not permitted for another employee, or refers to something absent.</summary>
    Rejected,

    /// <summary>A definite failure. Nothing happened.</summary>
    Failed,

    /// <summary>An indeterminate failure. It may or may not have happened — never claim either.</summary>
    Indeterminate,
}

public sealed record ToolResult<T>(ToolOutcome Outcome, T? Value, string? Message)
{
    public bool IsSuccess => Outcome == ToolOutcome.Success;

    public static ToolResult<T> Ok(T value) => new(ToolOutcome.Success, value, null);

    public static ToolResult<T> Denied(string message) =>
        new(ToolOutcome.PermissionDenied, default, message);

    public static ToolResult<T> ConfirmationRequired(string message) =>
        new(ToolOutcome.ConfirmationRequired, default, message);

    public static ToolResult<T> Rejected(string message) =>
        new(ToolOutcome.Rejected, default, message);

    public static ToolResult<T> Failed(string message) =>
        new(ToolOutcome.Failed, default, message);

    public static ToolResult<T> Indeterminate(string message) =>
        new(ToolOutcome.Indeterminate, default, message);
}
