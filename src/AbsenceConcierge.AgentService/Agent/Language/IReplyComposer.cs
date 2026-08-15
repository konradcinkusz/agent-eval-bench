namespace AbsenceConcierge.AgentService.Agent.Language;

/// <summary>
/// Turns the turn's typed state into the sentence the user reads.
///
/// <para>
/// The agent's <em>decision</em> is already made and already recorded as a trace
/// attribute by the time this runs (ADR-0003). Nothing here can change an outcome,
/// call a tool or affect a constraint — which is what makes it safe for the other
/// implementation of this interface to be a language model, and what makes Layer 1
/// indifferent to which one ran.
/// </para>
/// <para>
/// It is also the boundary C-3 is enforced at: no internal identifier reaches user-
/// facing output, so the composer works from names and dates and never from ids.
/// </para>
/// </summary>
public interface IReplyComposer
{
    /// <summary>
    /// Asynchronous because one implementation calls a model over a network. The
    /// deterministic one completes synchronously and pays nothing for the signature;
    /// the alternative — a synchronous interface with a model behind it — is a
    /// blocked thread pool under the only load this service will ever see.
    /// </summary>
    ValueTask<string> ComposeAsync(
        AgentTurnContext context,
        string outcome,
        CancellationToken cancellationToken = default);
}
