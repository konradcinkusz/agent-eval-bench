using System.Collections.Concurrent;
using AbsenceConcierge.AgentService.Workforce;

namespace AbsenceConcierge.AgentService.Agent;

/// <summary>
/// What survives between the turns of one conversation, and nothing more.
///
/// <para>
/// The list is short by design. A draft waiting on a decision, the token bound to
/// it, and the leave types actually retrieved in this conversation — that last one
/// is not a cache, it is the grounding record C-5 is checked against: an identifier
/// in a write must have come from a tool result, and this is where "came from a
/// tool result" is written down.
/// </para>
/// <para>
/// Nothing survives between conversations (SPEC §9). A confirmation cannot be
/// carried across sessions, and there is no store to carry it in.
/// </para>
/// </summary>
public sealed class AgentConversation(string id)
{
    private readonly HashSet<string> _retrievedLeaveTypeIds = new(StringComparer.Ordinal);

    public string Id => id;

    public int TurnIndex { get; private set; }

    public LeaveDraft? PendingDraft { get; private set; }

    public string? PendingConfirmationToken { get; private set; }

    public int NextTurn() => ++TurnIndex;

    public void RecordRetrievedLeaveTypes(IEnumerable<LeaveType> leaveTypes)
    {
        foreach (var leaveType in leaveTypes)
        {
            _retrievedLeaveTypeIds.Add(leaveType.Id);
        }
    }

    /// <summary>
    /// Whether this identifier was produced by a tool in this conversation. The
    /// agent asks before it writes, so a hallucinated identifier fails inside the
    /// agent as well as at the boundary and in the trace — three layers, because
    /// "confidently wrong" is the failure a judge does not catch (C-5).
    /// </summary>
    public bool IsGrounded(string leaveTypeId) => _retrievedLeaveTypeIds.Contains(leaveTypeId);

    public void HoldDraft(LeaveDraft draft, string token)
    {
        PendingDraft = draft;
        PendingConfirmationToken = token;
    }

    /// <summary>
    /// Clears the pending draft, returning what was held. Called on approval and on
    /// rejection alike: a decision consumes the draft either way, so a second
    /// "yes" cannot resubmit the first draft (C-6, enforced here as well as by the
    /// token store's single-use rule).
    /// </summary>
    public (LeaveDraft? Draft, string? Token) TakeDraft()
    {
        var held = (PendingDraft, PendingConfirmationToken);
        PendingDraft = null;
        PendingConfirmationToken = null;
        return held;
    }
}

public interface IAgentConversationStore
{
    AgentConversation GetOrCreate(string conversationId);
}

public sealed class InMemoryAgentConversationStore : IAgentConversationStore
{
    private readonly ConcurrentDictionary<string, AgentConversation> _conversations =
        new(StringComparer.Ordinal);

    public AgentConversation GetOrCreate(string conversationId) =>
        _conversations.GetOrAdd(conversationId, id => new AgentConversation(id));
}
