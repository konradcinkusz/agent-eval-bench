using AbsenceConcierge.AgentService.Workforce.Confirmation;
using AbsenceConcierge.AgentService.Workforce.Fixtures;

namespace AbsenceConcierge.AgentService.Workforce.Mock;

/// <summary>
/// The demonstrated path: an in-memory workforce backed by a fixture file. Zero
/// credentials, deterministic, and the implementation every eval scenario and the
/// public demo run against (ADR-0002).
///
/// This is a <em>test seam built into the service</em>, not a fake living in the test
/// project. That distinction is the estate's rule and it matters here: a parallel fake
/// would let the eval suite pass against behaviour the deployed service does not have.
///
/// It enforces, independently of anything the agent decides:
///   • the actor's permissions — the fixture is the authority, and a string in a tool
///     result claiming otherwise is just a string;
///   • only-for-self on reads and writes;
///   • a valid, approved, single-use confirmation token on every write.
/// The agent's good behaviour is UX; this class is the boundary.
/// </summary>
public sealed class MockWorkforceTools(
    WorkforceWorld world,
    IConfirmationTokenStore confirmationTokens,
    TimeProvider timeProvider) : IWorkforceTools
{
    private int _requestSequence;

    public ValueTask<ToolResult<WorkforceUser>> GetCurrentUserAsync(CancellationToken cancellationToken = default) =>
        ValueTask.FromResult(ToolResult<WorkforceUser>.Ok(world.Actor));

    public ValueTask<ToolResult<IReadOnlyList<Employee>>> FindEmployeeAsync(
        string nameQuery,
        CancellationToken cancellationToken = default)
    {
        if (Denied<IReadOnlyList<Employee>>(WorkforceToolCatalog.FindEmployee) is { } denied)
        {
            return ValueTask.FromResult(denied);
        }

        if (string.IsNullOrWhiteSpace(nameQuery))
        {
            return ValueTask.FromResult(
                ToolResult<IReadOnlyList<Employee>>.Rejected("A name to search for is required."));
        }

        // Substring match, deliberately. Two colleagues named Sam Rivera both match
        // "Sam", and returning both is the whole point — the ambiguity is real data,
        // not a special case the mock invents.
        IReadOnlyList<Employee> matches = world.Employees
            .Where(e => e.DisplayName.Contains(nameQuery, StringComparison.OrdinalIgnoreCase))
            .ToList();

        return ValueTask.FromResult(ToolResult<IReadOnlyList<Employee>>.Ok(matches));
    }

    public ValueTask<ToolResult<IReadOnlyList<LeaveType>>> ListLeaveTypesAsync(
        CancellationToken cancellationToken = default)
    {
        if (Denied<IReadOnlyList<LeaveType>>(WorkforceToolCatalog.ListLeaveTypes) is { } denied)
        {
            return ValueTask.FromResult(denied);
        }

        return ValueTask.FromResult(ToolResult<IReadOnlyList<LeaveType>>.Ok(world.LeaveTypes));
    }

    public ValueTask<ToolResult<IReadOnlyList<Leave>>> ListLeavesAsync(
        CancellationToken cancellationToken = default)
    {
        if (Denied<IReadOnlyList<Leave>>(WorkforceToolCatalog.ListLeaves) is { } denied)
        {
            return ValueTask.FromResult(denied);
        }

        // only_for_self. A colleague's bookings are not the actor's business, and the
        // fixture deliberately contains one (lv-3003) as a distractor.
        IReadOnlyList<Leave> mine = world.ExistingLeaves
            .Where(l => string.Equals(l.EmployeeId, world.Actor.EmployeeId, StringComparison.Ordinal))
            .ToList();

        return ValueTask.FromResult(ToolResult<IReadOnlyList<Leave>>.Ok(mine));
    }

    public ValueTask<ToolResult<TimeOffResult>> RequestTimeOffAsync(
        TimeOffRequest request,
        CancellationToken cancellationToken = default)
    {
        if (Denied<TimeOffResult>(WorkforceToolCatalog.RequestTimeOff) is { } denied)
        {
            return ValueTask.FromResult(denied);
        }

        // ── The gate, enforced here rather than trusted upstream ──────────────
        // Checked FIRST, before argument validation, so that an unconfirmed write is
        // refused for being unconfirmed rather than for happening to be malformed.
        // A scenario asserting the gate must fail for the right reason.
        var draft = new ConfirmationDraft(
            world.Actor.EmployeeId,
            request.LeaveTypeId,
            request.StartDate,
            request.EndDate);

        if (!confirmationTokens.TryRedeem(request.ConfirmationToken, draft))
        {
            return ValueTask.FromResult(ToolResult<TimeOffResult>.ConfirmationRequired(
                "This request has not been confirmed by the employee, or the confirmation "
                + "does not match what is being submitted."));
        }

        if (world.LeaveTypes.All(t => !string.Equals(t.Id, request.LeaveTypeId, StringComparison.Ordinal)))
        {
            // An id that never came from list_leave_types. C-5 catches this in the
            // trace; the boundary catches it in reality.
            return ValueTask.FromResult(ToolResult<TimeOffResult>.Rejected(
                "That leave type does not exist."));
        }

        if (request.EndDate < request.StartDate)
        {
            return ValueTask.FromResult(ToolResult<TimeOffResult>.Rejected(
                "The end date is before the start date."));
        }

        var today = DateOnly.FromDateTime(timeProvider.GetUtcNow().UtcDateTime);
        if (request.StartDate < today)
        {
            return ValueTask.FromResult(ToolResult<TimeOffResult>.Rejected(
                "Time off cannot be requested for a date in the past."));
        }

        var requestId = $"req-{1000 + Interlocked.Increment(ref _requestSequence)}";

        return ValueTask.FromResult(ToolResult<TimeOffResult>.Ok(
            new TimeOffResult(requestId, "pending_approval", request.StartDate, request.EndDate)));
    }

    /// <summary>
    /// Returns a denial result when the actor lacks the tool's required permission,
    /// or <c>null</c> when the call may proceed. The permission comes from the
    /// catalogue and the grant from the fixture; neither is derived from anything the
    /// agent says or anything a previous tool result claimed.
    /// </summary>
    private ToolResult<T>? Denied<T>(string toolName)
    {
        var required = WorkforceToolCatalog.RequiredPermission(toolName);

        if (required is null || world.Actor.Permissions.Contains(required, StringComparer.Ordinal))
        {
            return null;
        }

        // The message names the capability in plain language and never the permission
        // string, which is an internal identifier (SPEC C-3, O-7).
        var capability = toolName switch
        {
            WorkforceToolCatalog.RequestTimeOff => "request time off",
            WorkforceToolCatalog.ListLeaves => "see your time off",
            WorkforceToolCatalog.ListLeaveTypes => "see the kinds of leave available to you",
            WorkforceToolCatalog.FindEmployee => "look people up in the directory",
            _ => "do that",
        };

        return ToolResult<T>.Denied($"You do not have permission to {capability}.");
    }
}
