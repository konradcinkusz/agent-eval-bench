using System.Collections.Concurrent;

namespace AbsenceConcierge.AgentService.Workforce;

/// <summary>
/// Makes one named tool fail, in a named way, optionally after succeeding a few
/// times first.
///
/// <para>
/// One mechanism, two callers. The eval harness drives it from a scenario's
/// <c>tool_behaviour</c> block; the browser suite drives it from configuration, so
/// the two card states that only appear when a tool fails can be seen through the
/// glass rather than only in-process. A second injector would be a second set of
/// semantics for "the backend returned 500", and the one the suite asserts against
/// would be the one nobody ships.
/// </para>
///
/// <para>
/// It sits <b>beneath</b> the instrumentation decorator, so an injected failure still
/// produces one span carrying its attempt events — which is what
/// <c>call_attempts</c> reads, and what makes "never a silent retry loop" checkable
/// rather than assumed.
/// </para>
/// <para>
/// <b>Latency is not slept through.</b> <c>deg-001</c> declares 30 seconds;
/// honouring that literally would spend more than the entire Layer 1 budget on one
/// scenario, and a suite over its budget gets pruned rather than renamed
/// (SPEC §8.1). The field documents what is being modelled; the outcome is what the
/// scenario asserts, and it arrives immediately.
/// </para>
/// </summary>
public sealed class FaultInjectingWorkforceTools(
    IWorkforceTools inner,
    IReadOnlyDictionary<string, ToolFault> behaviours) : IWorkforceTools
{
    private readonly ConcurrentDictionary<string, int> _calls = new(StringComparer.Ordinal);

    public ValueTask<ToolResult<WorkforceUser>> GetCurrentUserAsync(CancellationToken cancellationToken = default) =>
        Intercept(WorkforceToolCatalog.GetCurrentUser)
            is { } fault
            ? ValueTask.FromResult(Apply<WorkforceUser>(fault, empty: default))
            : inner.GetCurrentUserAsync(cancellationToken);

    public ValueTask<ToolResult<IReadOnlyList<Employee>>> FindEmployeeAsync(
        string nameQuery,
        CancellationToken cancellationToken = default) =>
        Intercept(WorkforceToolCatalog.FindEmployee)
            is { } fault
            ? ValueTask.FromResult(Apply<IReadOnlyList<Employee>>(fault, empty: []))
            : inner.FindEmployeeAsync(nameQuery, cancellationToken);

    public ValueTask<ToolResult<IReadOnlyList<LeaveType>>> ListLeaveTypesAsync(
        CancellationToken cancellationToken = default) =>
        Intercept(WorkforceToolCatalog.ListLeaveTypes)
            is { } fault
            ? ValueTask.FromResult(Apply<IReadOnlyList<LeaveType>>(fault, empty: []))
            : inner.ListLeaveTypesAsync(cancellationToken);

    public ValueTask<ToolResult<IReadOnlyList<Leave>>> ListLeavesAsync(
        CancellationToken cancellationToken = default) =>
        Intercept(WorkforceToolCatalog.ListLeaves)
            is { } fault
            ? ValueTask.FromResult(Apply<IReadOnlyList<Leave>>(fault, empty: []))
            : inner.ListLeavesAsync(cancellationToken);

    public ValueTask<ToolResult<TimeOffResult>> RequestTimeOffAsync(
        TimeOffRequest request,
        CancellationToken cancellationToken = default) =>
        Intercept(WorkforceToolCatalog.RequestTimeOff)
            is { } fault
            ? ValueTask.FromResult(Apply<TimeOffResult>(fault, empty: default))
            : inner.RequestTimeOffAsync(request, cancellationToken);

    /// <summary>
    /// Returns the behaviour to apply, or null when this call should reach the real
    /// tool. Counts every call to the named tool, including the ones it lets through,
    /// because <c>after_calls</c> is about position in the sequence.
    /// </summary>
    private ToolFault? Intercept(string toolName)
    {
        if (!behaviours.TryGetValue(toolName, out var behaviour)
            || string.Equals(behaviour.Outcome, "success", StringComparison.Ordinal))
        {
            return null;
        }

        var call = _calls.AddOrUpdate(toolName, 1, (_, previous) => previous + 1);

        return call > behaviour.AfterCalls ? behaviour : null;
    }

    private static ToolResult<T> Apply<T>(ToolFault behaviour, T? empty) => behaviour.Outcome switch
    {
        // May or may not have happened. The agent must never claim either
        // (SPEC §7.2), and must never retry it on a write.
        "timeout" => ToolResult<T>.Indeterminate("The request timed out."),

        "http_500" => ToolResult<T>.Failed("The backend returned an error."),
        "http_429" => ToolResult<T>.Failed("The backend is rate limiting."),

        // Distinct from a permission fixture: the backend refusing at call time,
        // not the agent never having been entitled to call.
        "http_403" => ToolResult<T>.Denied("The backend refused the call."),

        // A successful call that answered nothing. Modelled as success on purpose —
        // the agent must not report it as a user error (deg-005).
        "empty" => ToolResult<T>.Ok(empty!),

        // KNOWN LIMIT. The agent has no separate outcome for a response it could not
        // parse, so this arrives as a definite failure and `degradation.kind` reads
        // `error` rather than `malformed`. No scenario uses it today; the first one
        // that does needs a ToolOutcome before it needs a scenario, and this comment
        // is here so that arrives as a decision rather than a surprise.
        "malformed" => ToolResult<T>.Failed("The backend returned something unreadable."),

        _ => throw new ArgumentOutOfRangeException(
            nameof(behaviour),
            behaviour.Outcome,
            "Unknown tool_behaviour outcome. The schema's enum and this switch must agree."),
    };
}
