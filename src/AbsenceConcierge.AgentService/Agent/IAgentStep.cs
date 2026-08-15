namespace AbsenceConcierge.AgentService.Agent;

/// <summary>Whether the pipeline carries on after a step, or the turn is over.</summary>
public enum StepSignal
{
    Continue,
    Stop,
}

/// <summary>
/// One link in the agent's chain.
///
/// <para>
/// The pipeline is a fixed, ordered list of these — resolve who is asking, read the
/// decision if there is one, understand the request, refuse it if it is out of
/// scope, resolve the dates, retrieve the leave types, check for conflicts, draft,
/// gate, execute, reply. It is deliberately not a model deciding what to do next.
/// The order is the specification, it is reviewable as a list, and every constraint
/// in SPEC §4 is a property of this ordering rather than a hope about a prompt.
/// </para>
/// <para>
/// Extensibility is interface plus a registration line (P10): a new step is a class
/// and a position in the list, not a change to the orchestrator.
/// </para>
/// </summary>
public interface IAgentStep
{
    /// <summary>Stable name. Appears in the trace, so renaming one is reviewed as a contract change.</summary>
    string Name { get; }

    /// <summary>
    /// Whether this step has anything to do on this turn.
    ///
    /// Separate from <see cref="ExecuteAsync"/> so that "did not apply" and "ran and
    /// did nothing" are different things in the trace. A confirmation turn skips
    /// seven steps, and a reader of the trace should be able to see that it skipped
    /// them rather than infer it from silence.
    /// </summary>
    bool AppliesTo(AgentTurnContext context);

    ValueTask<StepSignal> ExecuteAsync(AgentTurnContext context, CancellationToken cancellationToken);
}
