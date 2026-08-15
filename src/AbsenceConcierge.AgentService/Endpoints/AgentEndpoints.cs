using AbsenceConcierge.AgentService.Agent;
using AbsenceConcierge.AgentService.Demo;
using AbsenceConcierge.AgentService.Extensions;
using Microsoft.AspNetCore.RateLimiting;

namespace AbsenceConcierge.AgentService.Endpoints;

/// <param name="ConversationId">
/// Groups turns. A confirmation only resolves a draft held by the same conversation,
/// and nothing is carried between conversations (SPEC §9).
/// </param>
/// <param name="Message">What the user said, or the words accompanying a decision.</param>
/// <param name="Decision">
/// <c>approve</c> or <c>reject</c>. Present <b>only</b> on a confirmation turn.
/// A typed field rather than a sentence to be interpreted, so that a persuasive
/// message can never stand in for an explicit approval.
/// </param>
public sealed record AgentTurnPayload(string ConversationId, string Message, string? Decision);

/// <param name="Turn">What the agent decided, and what it said.</param>
/// <param name="Mode">
/// Which composer answered and why. Returned on every turn rather than fetched
/// separately, so the page's banner can never describe a different turn from the one
/// it is showing.
/// </param>
public sealed record AgentTurnEnvelope(AgentTurnResult Turn, DemoStatus Mode);

/// <summary>
/// Transport only: bind, delegate, map the result.
///
/// One route for the agent, because the agent has one operation. The write still has
/// no HTTP route of its own — the only path to <c>request_time_off</c> runs through
/// the pipeline and its gate, and a convenience endpoint would hand every adversarial
/// scenario a way around the thing it is testing.
/// </summary>
public static class AgentEndpoints
{
    /// <summary>
    /// Where the demo's access code arrives. A header rather than a query parameter
    /// or a body field: query strings are logged by proxies and pasted into chat
    /// windows, and this one is a spend control that a screenshot should not leak.
    /// </summary>
    public const string AccessCodeHeader = "X-Demo-Access-Code";

    /// <summary>
    /// The longest message the demo accepts.
    ///
    /// <para>
    /// A sentence about being ill is under two hundred characters. This is generous
    /// enough that no honest visitor meets it and small enough that the interpreter,
    /// the trace and — when live mode is unlocked — the model's input all have a
    /// bound that does not depend on anybody being reasonable.
    /// </para>
    /// </summary>
    private const int MaxMessageLength = 1000;

    public static WebApplication MapAgentEndpoints(this WebApplication app)
    {
        app.MapPost("/agent/turn", async (
            AgentTurnPayload payload,
            HttpContext http,
            IAgentOrchestrator orchestrator,
            DemoAccess access,
            CancellationToken cancellationToken) =>
        {
            if (string.IsNullOrWhiteSpace(payload.ConversationId))
            {
                return Results.BadRequest(new { error = "A conversation id is required." });
            }

            if ((payload.Message?.Length ?? 0) > MaxMessageLength)
            {
                return Results.BadRequest(new { error = "That message is longer than this demo accepts." });
            }

            if (!TryReadDecision(payload.Decision, out var decision))
            {
                return Results.BadRequest(new { error = "Decision must be 'approve' or 'reject'." });
            }

            var mode = access.Evaluate(http.Request.Headers[AccessCodeHeader].ToString());

            var result = await orchestrator.RunTurnAsync(
                new AgentTurnRequest(
                    payload.ConversationId,
                    payload.Message ?? string.Empty,
                    decision,

                    // The single place UseModel is ever set. A model may write this
                    // turn's prose; nothing about the decision changes, because the
                    // composer runs after every step has already run.
                    UseModel: mode.Live),
                cancellationToken);

            return Results.Ok(new AgentTurnEnvelope(result, mode));
        })
        .WithTags("Agent")
        .RequireRateLimiting(ServiceCollectionExtensions.DemoRateLimitPolicy);

        // What the page asks on load: whether unlocking is even possible on this
        // deployment, and how much budget is left. It reports the same four states
        // the turn endpoint does, from the same code.
        app.MapGet("/demo/status", (HttpContext http, DemoAccess access) =>
            Results.Ok(access.Evaluate(http.Request.Headers[AccessCodeHeader].ToString())))
            .WithTags("Agent")
            .RequireRateLimiting(ServiceCollectionExtensions.DemoRateLimitPolicy);

        return app;
    }

    private static bool TryReadDecision(string? value, out ConfirmationDecision? decision)
    {
        decision = null;

        if (string.IsNullOrWhiteSpace(value))
        {
            return true;
        }

        if (string.Equals(value, "approve", StringComparison.OrdinalIgnoreCase))
        {
            decision = ConfirmationDecision.Approve;
            return true;
        }

        if (string.Equals(value, "reject", StringComparison.OrdinalIgnoreCase))
        {
            decision = ConfirmationDecision.Reject;
            return true;
        }

        // Rejected rather than coerced. A decision the service could not read must
        // not become an approval, and must not silently become an ordinary message
        // either — the caller meant something, and guessing which is how a gate
        // becomes a formality.
        return false;
    }
}
