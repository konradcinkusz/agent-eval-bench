using System.Collections.Concurrent;
using System.Security.Cryptography;

namespace AbsenceConcierge.AgentService.Workforce.Confirmation;

/// <summary>
/// A draft that has been shown to a human and is waiting on their decision.
/// </summary>
public sealed record ConfirmationDraft(
    string EmployeeId,
    string LeaveTypeId,
    DateOnly StartDate,
    DateOnly EndDate);

/// <summary>
/// Mints and redeems the token that <see cref="TimeOffRequest.ConfirmationToken"/>
/// carries.
///
/// This is the second enforcement layer, and it is the reason the confirmation gate
/// is a property of the system rather than a habit of the prompt. The agent may be
/// argued into attempting an unconfirmed write by a prompt injection; it cannot be
/// argued into producing a token that was never issued.
/// </summary>
public interface IConfirmationTokenStore
{
    /// <summary>Issued when <c>confirmation.shown</c> is emitted. Not yet valid for a write.</summary>
    string Issue(ConfirmationDraft draft);

    /// <summary>Called when a human approves. Only now can the token authorise a write.</summary>
    bool Approve(string token);

    /// <summary>
    /// Redeems a token for exactly one write, checking it matches the draft being
    /// submitted. Single-use: this is what makes C-6 ("at most one write per
    /// confirmation") a property of the boundary rather than of the agent's restraint.
    /// </summary>
    bool TryRedeem(string token, ConfirmationDraft submitted);

    /// <summary>
    /// Called when a human declines. Until this existed the store had no terminal
    /// state but redemption: entries left only on a successful write, so every
    /// declined draft kept one for the process lifetime. The A6 state machine names
    /// the edge now that the code has it.
    /// </summary>
    void Reject(string token);
}

/// <summary>
/// The in-memory store, bounded.
///
/// <para>
/// It was the one map in this service with neither a bound nor an expiry, in the
/// component the documentation elevates most. Entries left only on redemption, so a
/// rejected confirmation, an abandoned card and an evicted conversation each leaked
/// one for the process lifetime — while the conversation store and the client-quota
/// store are both explicitly bounded, with comments explaining that a public
/// endpoint plus an unbounded map is a memory exhaustion anybody can cause.
/// </para>
/// <para>
/// Two changes close it: <see cref="Reject"/> removes the entry when a human
/// declines, which gives the store a terminal state other than a successful write;
/// and past the cap the oldest issued token is evicted. Eviction fails closed — an evicted token redeems as false, so the write
/// is refused and the visitor is re-asked. A lost draft is a re-ask, never a write,
/// which is the same direction the conversation store's eviction fails in.
/// </para>
/// </summary>
public sealed class InMemoryConfirmationTokenStore(int? capacity = null) : IConfirmationTokenStore
{
    /// <summary>Matches the conversation cap: each conversation holds at most one pending draft.</summary>
    public const int DefaultCapacity = 10_000;

    private sealed record Entry(ConfirmationDraft Draft, bool Approved, long Issued);

    private readonly ConcurrentDictionary<string, Entry> _entries = new(StringComparer.Ordinal);
    private readonly Lock _evicting = new();
    private readonly int _capacity = Math.Max(1, capacity ?? DefaultCapacity);

    private long _stamp;

    public string Issue(ConfirmationDraft draft)
    {
        // A GUID is not a secret (SECURITY-REVIEW §5). This token is presented as
        // proof that a human approved something, which puts it squarely in the
        // "≥256 bits from a CSPRNG" category rather than the identifier category.
        var token = Convert.ToBase64String(RandomNumberGenerator.GetBytes(32))
            .Replace('+', '-')
            .Replace('/', '_')
            .TrimEnd('=');

        _entries[token] = new Entry(draft, Approved: false, Issued: Interlocked.Increment(ref _stamp));

        if (_entries.Count > _capacity)
        {
            Evict();
        }

        return token;
    }

    public void Reject(string token)
    {
        if (!string.IsNullOrEmpty(token))
        {
            _entries.TryRemove(token, out _);
        }
    }

    public bool Approve(string token)
    {
        // Compare-and-swap rather than read-then-set. The indexer version had a
        // hole exactly where this store is the last line of defence: between the
        // read and the write-back, a concurrent TryRedeem could remove the token —
        // and the write-back would then re-insert it, approved, resurrecting a
        // spent token for a second write. Nothing above this layer serialises
        // turns, so "two approvals racing one redeem" is a pair of parallel HTTP
        // requests, not a hypothetical. TryUpdate refuses to insert: once the
        // token is gone, it stays gone.
        while (_entries.TryGetValue(token, out var entry))
        {
            if (entry.Approved)
            {
                return true;
            }

            if (_entries.TryUpdate(token, entry with { Approved = true }, entry))
            {
                return true;
            }
        }

        return false;
    }

    public bool TryRedeem(string token, ConfirmationDraft submitted)
    {
        if (string.IsNullOrEmpty(token) || !_entries.TryGetValue(token, out var entry))
        {
            return false;
        }

        if (!entry.Approved)
        {
            // Shown but not approved. This is the injection case: the agent reached
            // the gate, was talked into submitting anyway, and is refused here.
            return false;
        }

        // The token authorises one specific draft. Approving a two-day sick request
        // does not authorise a two-week holiday, however the arguments were arrived at.
        if (entry.Draft != submitted)
        {
            return false;
        }

        // Single use, and removal is atomic so a concurrent double-submit loses.
        return _entries.TryRemove(token, out _);
    }

    private void Evict()
    {
        lock (_evicting)
        {
            // Down to the cap, oldest issued first — the same shape the conversation
            // store uses, and for the same reason: a linear scan per eviction is fine
            // at this size, and only somebody already past the cap ever pays for it.
            // A monotonic stamp rather than a clock, because eviction needs an order
            // and the wall clock can move backwards.
            while (_entries.Count > _capacity)
            {
                string? oldestKey = null;
                var oldestStamp = long.MaxValue;

                foreach (var (key, entry) in _entries)
                {
                    if (entry.Issued < oldestStamp)
                    {
                        oldestStamp = entry.Issued;
                        oldestKey = key;
                    }
                }

                if (oldestKey is null || !_entries.TryRemove(oldestKey, out _))
                {
                    break;
                }
            }
        }
    }
}
