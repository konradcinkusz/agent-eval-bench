namespace AbsenceConcierge.AgentService.Agent.Language;

/// <summary>
/// Turns what the user said into a typed <see cref="Intent"/>.
///
/// <para>
/// This is the agent's <b>only</b> language seam on the way in, and it is an
/// interface for one reason: the implementation behind it is what changes when a
/// model is wired up, and nothing downstream of it should notice. The orchestrator,
/// the steps, the gate, the constraint layer and every Layer 1 assertion are
/// written against <see cref="Intent"/>, not against a model.
/// </para>
/// <para>
/// Which implementation ran is recorded on the turn span as
/// <c>agent.interpreter</c>. That attribute is not decoration: a baseline recorded
/// against one interpreter does not describe the other, and an eval suite that
/// silently mixed them would be measuring with a ruler that changes length
/// (see ADR-0004).
/// </para>
/// </summary>
public interface IUtteranceInterpreter
{
    /// <summary>Short, stable name for the trace. Not a version string.</summary>
    string Name { get; }

    ValueTask<Intent> InterpretAsync(string utterance, CancellationToken cancellationToken = default);
}
