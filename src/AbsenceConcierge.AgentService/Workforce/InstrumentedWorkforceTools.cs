using System.Diagnostics;
using System.Globalization;
using AbsenceConcierge.AgentService.Telemetry;

namespace AbsenceConcierge.AgentService.Workforce;

/// <summary>
/// Emits the tool-call span that Layer 1 asserts against, for any
/// <see cref="IWorkforceTools"/> implementation.
///
/// A decorator rather than instrumentation inside each implementation, for two
/// reasons. The mock and the MCP adapter must produce byte-identical trace shapes,
/// or a scenario that passes on the mock proves nothing about the real integration.
/// And the span is part of the eval contract, so it belongs somewhere a reviewer can
/// read in one file rather than scattered across every method of every backend.
///
/// ONE SPAN PER LOGICAL TOOL CALL (SPEC §2.2.1). Transport retries, when a resilience
/// handler adds them beneath this layer, are events on this span — never sibling
/// spans — so that <c>tool_called times: 1</c> means what a scenario author expects.
/// </summary>
public sealed class InstrumentedWorkforceTools(IWorkforceTools inner) : IWorkforceTools
{
    public ValueTask<ToolResult<WorkforceUser>> GetCurrentUserAsync(CancellationToken cancellationToken = default) =>
        TraceAsync(
            WorkforceToolCatalog.GetCurrentUser,
            arguments: null,
            () => inner.GetCurrentUserAsync(cancellationToken));

    public ValueTask<ToolResult<IReadOnlyList<Employee>>> FindEmployeeAsync(
        string nameQuery,
        CancellationToken cancellationToken = default) =>
        TraceAsync(
            WorkforceToolCatalog.FindEmployee,
            $"name_query={nameQuery}",
            () => inner.FindEmployeeAsync(nameQuery, cancellationToken));

    public ValueTask<ToolResult<IReadOnlyList<LeaveType>>> ListLeaveTypesAsync(
        CancellationToken cancellationToken = default) =>
        TraceAsync(
            WorkforceToolCatalog.ListLeaveTypes,
            arguments: null,
            () => inner.ListLeaveTypesAsync(cancellationToken));

    public ValueTask<ToolResult<IReadOnlyList<Leave>>> ListLeavesAsync(
        CancellationToken cancellationToken = default) =>
        TraceAsync(
            WorkforceToolCatalog.ListLeaves,
            arguments: null,
            () => inner.ListLeavesAsync(cancellationToken));

    public ValueTask<ToolResult<TimeOffResult>> RequestTimeOffAsync(
        TimeOffRequest request,
        CancellationToken cancellationToken = default) =>
        TraceAsync(
            WorkforceToolCatalog.RequestTimeOff,
            // The confirmation token is deliberately absent from the recorded
            // arguments: it authorises a write, so it is a credential, and a
            // credential does not belong in a trace that gets exported.
            string.Create(
                CultureInfo.InvariantCulture,
                $"leave_type_id={request.LeaveTypeId};start_date={request.StartDate:yyyy-MM-dd};end_date={request.EndDate:yyyy-MM-dd}"),
            () => inner.RequestTimeOffAsync(request, cancellationToken));

    private static async ValueTask<ToolResult<T>> TraceAsync<T>(
        string toolName,
        string? arguments,
        Func<ValueTask<ToolResult<T>>> call)
    {
        // GenAI semantic conventions: "{operation} {target}".
        using var activity = AgentDiagnostics.Source.StartActivity(
            $"execute_tool {toolName}",
            ActivityKind.Internal);

        activity?.SetTag(AgentDiagnostics.Attributes.OperationName, "execute_tool");
        activity?.SetTag(AgentDiagnostics.Attributes.ToolName, toolName);

        // From the catalogue, never inferred from the name (SPEC §2.1).
        activity?.SetTag(
            AgentDiagnostics.Attributes.ToolKind,
            WorkforceToolCatalog.KindOf(toolName) == WorkforceToolKind.Write ? "write" : "read");

        if (arguments is not null)
        {
            activity?.SetTag(AgentDiagnostics.Attributes.ToolArguments, arguments);
        }

        try
        {
            var result = await call().ConfigureAwait(false);

            activity?.SetTag(
                AgentDiagnostics.Attributes.ToolOutcome,
                result.Outcome.ToString().ToLowerInvariant());

            if (!result.IsSuccess)
            {
                // A refusal is not an exception, but it is not a success either, and a
                // trace that renders both the same makes a denied path indistinguishable
                // from a happy one at a glance.
                activity?.SetStatus(ActivityStatusCode.Error, result.Message);
            }

            return result;
        }
        catch (Exception ex)
        {
            // The span must record the failure before it propagates. An unobserved
            // exception here would leave a tool call in the trace with no outcome at
            // all, which reads as "still running" forever.
            activity?.SetTag(AgentDiagnostics.Attributes.ToolOutcome, "exception");
            activity?.SetStatus(ActivityStatusCode.Error, ex.Message);
            throw;
        }
    }
}
