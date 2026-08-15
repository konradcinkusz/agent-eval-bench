using AbsenceConcierge.AgentService.Agent;

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

/// <summary>
/// Transport only: bind, delegate, map the result.
///
/// One route, because the agent has one operation. The write still has no HTTP route
/// of its own — the only path to <c>request_time_off</c> runs through the pipeline
/// and its gate, and a convenience endpoint would hand every adversarial scenario a
/// way around the thing it is testing.
/// </summary>
public static class AgentEndpoints
{
    public static WebApplication MapAgentEndpoints(this WebApplication app)
    {
        app.MapPost("/agent/turn", async (
            AgentTurnPayload payload,
            IAgentOrchestrator orchestrator,
            CancellationToken cancellationToken) =>
        {
            if (string.IsNullOrWhiteSpace(payload.ConversationId))
            {
                return Results.BadRequest(new { error = "A conversation id is required." });
            }

            if (!TryReadDecision(payload.Decision, out var decision))
            {
                return Results.BadRequest(new { error = "Decision must be 'approve' or 'reject'." });
            }

            var result = await orchestrator.RunTurnAsync(
                new AgentTurnRequest(payload.ConversationId, payload.Message ?? string.Empty, decision),
                cancellationToken);

            return Results.Ok(result);
        })
        .WithTags("Agent");

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
