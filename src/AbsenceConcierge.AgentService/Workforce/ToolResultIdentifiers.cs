namespace AbsenceConcierge.AgentService.Workforce;

/// <summary>
/// The identifiers a tool result handed back, for the trace.
///
/// <para>
/// This exists because C-5 — "every identifier argument in a write appeared in an
/// earlier tool result in the same trace" — was specified before anything recorded
/// what a tool result contained. The span carried the call's <em>arguments</em> and
/// its outcome, so an assertion about grounding had nowhere to look, and the only
/// ways to evaluate it were to read the agent's memory (which is not the trace) or
/// to trust it (which is not an assertion). Writing the eval harness is what
/// surfaced that; the fix is here rather than in the harness because Layer 1 asserts
/// over the trace and nothing else (ADR-0003).
/// </para>
/// <para>
/// Identifiers only, never display text. A leave type's <em>name</em> can carry an
/// injected instruction and has no business in an attribute the harness reads;
/// its id is opaque, synthetic, and exactly what grounding is about.
/// </para>
/// </summary>
internal static class ToolResultIdentifiers
{
    public static string? Of(object? value) => value switch
    {
        WorkforceUser user => user.EmployeeId,
        TimeOffResult written => written.RequestId,
        IEnumerable<Employee> employees => Join(employees.Select(employee => employee.EmployeeId)),
        IEnumerable<LeaveType> leaveTypes => Join(leaveTypes.Select(leaveType => leaveType.Id)),
        IEnumerable<Leave> leaves => Join(leaves.Select(leave => leave.Id)),

        // A type with no identifiers is not an error; it records nothing rather
        // than recording an empty string that would read as "returned nothing".
        _ => null,
    };

    private static string? Join(IEnumerable<string> ids)
    {
        var joined = string.Join(";", ids);
        return joined.Length == 0 ? null : joined;
    }
}
