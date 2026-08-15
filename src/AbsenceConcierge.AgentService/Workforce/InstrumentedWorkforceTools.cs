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
/// ONE SPAN PER LOGICAL TOOL CALL (SPEC §2.2.1). Retries happen <em>inside</em> this
/// span and are recorded as <c>attempt</c> events on it — never sibling spans — so
/// that <c>tool_called times: 1</c> means what a scenario author expects and
/// <c>call_attempts</c> counts what SPEC §7 rule 3 bounds.
///
/// The attempt loop lives here rather than a layer further out precisely because of
/// that rule: a retry outside the span would produce three spans for one call and
/// make "never a silent retry loop" uncheckable.
/// </summary>
public sealed class InstrumentedWorkforceTools(IWorkforceTools inner, IToolAttemptPolicy attempts)
    : IWorkforceTools
{
    /// <summary>
    /// Convenience for tests and for callers that only want the span shape. One
    /// attempt per call means the decorator adds tracing and nothing else.
    /// </summary>
    public InstrumentedWorkforceTools(IWorkforceTools inner)
        : this(inner, new ToolAttemptPolicy(maxReadAttempts: 1))
    {
    }

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

    /// <summary>
    /// Whether another attempt is allowed to change the answer.
    ///
    /// A permission denial, a rejection and a missing confirmation are decisions,
    /// not weather: retrying them produces the same answer and a noisier trace. Only
    /// a definite failure and an indeterminate one are worth another go, and the
    /// policy decides whether this tool gets one.
    /// </summary>
    private static bool IsWorthRetrying(ToolOutcome outcome) =>
        outcome is ToolOutcome.Failed or ToolOutcome.Indeterminate;

    private async ValueTask<ToolResult<T>> TraceAsync<T>(
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

        var maxAttempts = attempts.MaxAttempts(toolName);

        try
        {
            ToolResult<T> result;
            var attempt = 0;

            while (true)
            {
                attempt++;
                result = await call().ConfigureAwait(false);

                var outcome = result.Outcome.ToString().ToLowerInvariant();

                activity?.AddEvent(new ActivityEvent(
                    AgentDiagnostics.Events.ToolAttempt,
                    tags: new ActivityTagsCollection
                    {
                        [AgentDiagnostics.Attributes.AttemptNumber] = attempt,
                        [AgentDiagnostics.Attributes.AttemptOutcome] = outcome,
                    }));

                if (attempt >= maxAttempts || !IsWorthRetrying(result.Outcome))
                {
                    activity?.SetTag(AgentDiagnostics.Attributes.ToolOutcome, outcome);
                    break;
                }
            }

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
