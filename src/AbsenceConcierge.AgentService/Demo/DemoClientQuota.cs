using Microsoft.Extensions.Options;

namespace AbsenceConcierge.AgentService.Demo;

/// <summary>
/// The per-client daily allowance of live-composed turns.
/// </summary>
public interface IDemoClientQuota
{
    /// <summary>Turns this client has left today, without consuming one.</summary>
    int Remaining(string clientKey);

    /// <summary>
    /// Consumes one live turn for this client, or refuses. Refusing is a normal
    /// outcome — the turn still runs, on the deterministic composer.
    /// </summary>
    bool TryConsume(string clientKey);
}

/// <summary>
/// An in-memory per-client ledger, reset at UTC midnight.
///
/// <para>
/// This exists for the open-access demo: with no access code, the only thing
/// standing between one visitor and the whole day's shared token budget is this
/// counter. It does not replace the budget — the budget is the ceiling on the
/// bill; this is the fairness rule that stops one address spending all of it.
/// </para>
/// <para>
/// <b>Bounded in both dimensions.</b> The day rolls the same way the budget's
/// does, and the map of clients is capped: past <see cref="MaxTrackedClients"/>
/// distinct keys in one day, a new client is refused live mode rather than
/// tracked. Refusing is the fail-closed direction — the alternative is a map an
/// attacker can grow one spoofed address at a time until the process pages out.
/// A visitor refused here still gets the full demo, on the deterministic
/// composer, which is the state this service is happy in.
/// </para>
/// <para>
/// In memory, like the daily budget, and accepted for the same reason (D-13): a
/// restart resets the ledger, the ceiling that matters is the budget, and a
/// database for a demo buys accuracy this does not need.
/// </para>
/// </summary>
public sealed class DemoClientQuota(TimeProvider timeProvider, IOptions<DemoOptions> options) : IDemoClientQuota
{
    /// <summary>
    /// Distinct clients tracked per day. Far above what a demo sees, far below
    /// what would matter to the process — each entry is a short string and an int.
    /// </summary>
    internal const int MaxTrackedClients = 50_000;

    private readonly Lock _gate = new();
    private readonly int _perDay = Math.Max(0, options.Value.LiveTurnsPerClientPerDay);
    private readonly Dictionary<string, int> _spent = new(StringComparer.Ordinal);

    private DateOnly _day;

    public int Remaining(string clientKey)
    {
        ArgumentNullException.ThrowIfNull(clientKey);

        lock (_gate)
        {
            Roll();
            return Math.Max(0, _perDay - _spent.GetValueOrDefault(clientKey));
        }
    }

    public bool TryConsume(string clientKey)
    {
        ArgumentNullException.ThrowIfNull(clientKey);

        lock (_gate)
        {
            Roll();

            var used = _spent.GetValueOrDefault(clientKey);

            if (used >= _perDay)
            {
                return false;
            }

            if (used == 0 && _spent.Count >= MaxTrackedClients)
            {
                return false;
            }

            _spent[clientKey] = used + 1;
            return true;
        }
    }

    private void Roll()
    {
        var today = DateOnly.FromDateTime(timeProvider.GetUtcNow().UtcDateTime);

        if (_day != today)
        {
            _day = today;
            _spent.Clear();
        }
    }
}
