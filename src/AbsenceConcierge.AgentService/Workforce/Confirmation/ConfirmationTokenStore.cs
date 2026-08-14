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
}

public sealed class InMemoryConfirmationTokenStore : IConfirmationTokenStore
{
    private sealed record Entry(ConfirmationDraft Draft, bool Approved);

    private readonly ConcurrentDictionary<string, Entry> _entries = new(StringComparer.Ordinal);

    public string Issue(ConfirmationDraft draft)
    {
        // A GUID is not a secret (SECURITY-REVIEW §5). This token is presented as
        // proof that a human approved something, which puts it squarely in the
        // "≥256 bits from a CSPRNG" category rather than the identifier category.
        var token = Convert.ToBase64String(RandomNumberGenerator.GetBytes(32))
            .Replace('+', '-')
            .Replace('/', '_')
            .TrimEnd('=');

        _entries[token] = new Entry(draft, Approved: false);
        return token;
    }

    public bool Approve(string token)
    {
        if (!_entries.TryGetValue(token, out var entry))
        {
            return false;
        }

        _entries[token] = entry with { Approved = true };
        return true;
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
}
