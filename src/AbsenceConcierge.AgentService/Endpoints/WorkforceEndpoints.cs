using AbsenceConcierge.AgentService.Extensions;
using AbsenceConcierge.AgentService.Workforce;

namespace AbsenceConcierge.AgentService.Endpoints;

/// <summary>
/// Transport only: bind, delegate, map the result (P9's layering). No decision is
/// made here — every rule these endpoints appear to enforce is enforced in
/// <see cref="IWorkforceTools"/>'s implementation, which is the boundary.
///
/// Phase 2 exposes reads only. There is deliberately no HTTP route that submits a
/// time-off request: the write is reachable only through the agent loop and its
/// confirmation gate, and adding a convenience endpoint that bypasses both would
/// hand every adversarial scenario a way around the thing they are testing.
/// </summary>
public static class WorkforceEndpoints
{
    public static WebApplication MapWorkforceEndpoints(this WebApplication app)
    {
        var group = app.MapGroup("/workforce")
            .WithTags("Workforce")

            // The same policy as the agent's route. "Rate limiting present in most
            // services" is the recorded failure mode (SECURITY-REVIEW.md §9), and
            // these are reads a stranger can reach.
            .RequireRateLimiting(ServiceCollectionExtensions.DemoRateLimitPolicy);

        group.MapGet("/me", async (IWorkforceTools tools, CancellationToken ct) =>
        {
            var result = await tools.GetCurrentUserAsync(ct);
            return ToHttpResult(result);
        });

        group.MapGet("/leave-types", async (IWorkforceTools tools, CancellationToken ct) =>
        {
            var result = await tools.ListLeaveTypesAsync(ct);
            return ToHttpResult(result);
        });

        group.MapGet("/leaves", async (IWorkforceTools tools, CancellationToken ct) =>
        {
            var result = await tools.ListLeavesAsync(ct);
            return ToHttpResult(result);
        });

        return app;
    }

    /// <summary>
    /// Maps a tool outcome onto a status code.
    ///
    /// The message is the tool's, and the tool's messages are written for a human to
    /// read: no identifiers, no permission strings, no internals
    /// (SECURITY-REVIEW §6 — "error messages never leak internals").
    /// </summary>
    private static IResult ToHttpResult<T>(ToolResult<T> result) => result.Outcome switch
    {
        ToolOutcome.Success => Results.Ok(result.Value),
        ToolOutcome.PermissionDenied => Results.Json(new { error = result.Message }, statusCode: 403),
        ToolOutcome.ConfirmationRequired => Results.Json(new { error = result.Message }, statusCode: 409),
        ToolOutcome.Rejected => Results.Json(new { error = result.Message }, statusCode: 400),

        // A definite failure and an indeterminate one are different answers and get
        // different codes, because the caller must be able to tell "this did not
        // happen" from "this may or may not have happened" (SPEC §7.2).
        ToolOutcome.Failed => Results.Json(new { error = result.Message }, statusCode: 502),
        ToolOutcome.Indeterminate => Results.Json(new { error = result.Message }, statusCode: 504),

        _ => Results.Json(new { error = "Unexpected tool outcome." }, statusCode: 500),
    };
}
