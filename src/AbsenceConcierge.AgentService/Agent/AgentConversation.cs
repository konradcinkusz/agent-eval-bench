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

    /// <summary>
    /// The conversation if it exists, without creating one. A look, so a ceiling
    /// check does not itself allocate the thing it is deciding whether to allow.
    /// </summary>
    AgentConversation? Find(string conversationId);
}

/// <summary>
/// Conversations, held in memory and <b>bounded</b>.
///
/// <para>
/// Every conversation id a caller invents becomes an entry here, and the endpoint
/// is public with no accounts — so an unbounded map is a memory exhaustion anybody
/// can cause with a loop and a random string. Past the cap, the least recently
/// touched conversation is evicted. For an evicted visitor the effect is a fresh
/// conversation under their old id: a pending draft is gone and its confirmation
/// token dies with it, which fails in the safe direction — a lost draft is a
/// re-ask, never a write.
/// </para>
/// </summary>
public sealed class InMemoryAgentConversationStore(
    Microsoft.Extensions.Options.IOptions<Demo.DemoOptions> options) : IAgentConversationStore
{
    private sealed record Entry(AgentConversation Conversation)
    {
        // A monotonic recency stamp, not a clock: eviction needs an order, and the
        // wall clock can move backwards.
        public long Touched;
    }

    private readonly ConcurrentDictionary<string, Entry> _conversations = new(StringComparer.Ordinal);
    private readonly Lock _evicting = new();
    private readonly int _capacity = Math.Max(1, options.Value.MaxConversations);

    private long _stamp;

    public AgentConversation GetOrCreate(string conversationId)
    {
        var entry = _conversations.GetOrAdd(conversationId, id => new Entry(new AgentConversation(id)));
        entry.Touched = Interlocked.Increment(ref _stamp);

        if (_conversations.Count > _capacity)
        {
            Evict();
        }

        return entry.Conversation;
    }

    public AgentConversation? Find(string conversationId) =>
        _conversations.TryGetValue(conversationId, out var entry) ? entry.Conversation : null;

    private void Evict()
    {
        lock (_evicting)
        {
            // Down to the cap, oldest first. A linear scan per eviction is fine at
            // this size, and eviction only happens when somebody is already past
            // the cap — the ordinary path never pays for it.
            while (_conversations.Count > _capacity)
            {
                string? oldestKey = null;
                var oldestStamp = long.MaxValue;

                foreach (var (key, entry) in _conversations)
                {
                    if (entry.Touched < oldestStamp)
                    {
                        oldestStamp = entry.Touched;
                        oldestKey = key;
                    }
                }

                if (oldestKey is null || !_conversations.TryRemove(oldestKey, out _))
                {
                    break;
                }
            }
        }
    }
}
