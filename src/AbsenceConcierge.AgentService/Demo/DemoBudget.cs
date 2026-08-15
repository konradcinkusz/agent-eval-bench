using Microsoft.Extensions.Options;

namespace AbsenceConcierge.AgentService.Demo;

/// <param name="Spent">Output tokens spent so far today.</param>
/// <param name="Budget">The day's ceiling.</param>
/// <param name="Day">The UTC day these numbers belong to.</param>
public sealed record BudgetState(int Spent, int Budget, DateOnly Day)
{
    public int Remaining => Math.Max(0, Budget - Spent);

    public bool Exhausted => Remaining == 0;
}

/// <summary>
/// The day's spend, and the decision to stop.
/// </summary>
public interface IDemoBudget
{
    BudgetState State { get; }

    /// <summary>
    /// Reserves headroom for one reply, or refuses. Refusing is a normal outcome and
    /// the caller degrades; it is not an error.
    /// </summary>
    bool TryReserve(int outputTokens);

    /// <summary>
    /// Settles a reservation against what the model actually charged. Always called,
    /// including when the call failed — a failed generation can still have produced
    /// tokens, and a budget that only counts successes is a budget that undercounts
    /// exactly when something is going wrong.
    /// </summary>
    void Settle(int reserved, int actualOutputTokens);
}

/// <summary>
/// An in-memory daily ledger, reset at UTC midnight.
///
/// <para>
/// <b>In memory, and that is a stated limit rather than an oversight.</b> The public
/// deployment scales to zero, so a restart resets the count and a day's true ceiling
/// is "budget × number of cold starts". That is acceptable here because the ceiling
/// is small, the model is small, and the alternative — a database for a demo — buys
/// accuracy this does not need at a cost this repository has no other reason to pay.
/// It is recorded in <c>docs/DEVIATIONS.md</c> rather than left for a reader to
/// deduce from the absence of a store.
/// </para>
/// <para>
/// Reserve-then-settle rather than count-after: a burst of concurrent turns that
/// each checked a counter before any of them wrote to it would all pass the check.
/// The reservation is what makes the ceiling hold under concurrency.
/// </para>
/// </summary>
public sealed class DemoBudget(TimeProvider timeProvider, IOptions<DemoOptions> options) : IDemoBudget
{
    private readonly Lock _gate = new();
    private readonly int _budget = Math.Max(0, options.Value.DailyOutputTokenBudget);

    private DateOnly _day;
    private int _spent;

    public BudgetState State
    {
        get
        {
            lock (_gate)
            {
                Roll();
                return new BudgetState(_spent, _budget, _day);
            }
        }
    }

    public bool TryReserve(int outputTokens)
    {
        var wanted = Math.Max(0, outputTokens);

        lock (_gate)
        {
            Roll();

            if (_spent + wanted > _budget)
            {
                return false;
            }

            _spent += wanted;
            return true;
        }
    }

    public void Settle(int reserved, int actualOutputTokens)
    {
        lock (_gate)
        {
            Roll();

            // The reservation was an upper bound. Give back what was not used, and
            // never give back more than was taken — a model that reports more output
            // than the ceiling allowed is a model whose numbers must not be able to
            // credit the budget.
            var correction = Math.Clamp(actualOutputTokens, 0, reserved) - reserved;
            _spent = Math.Max(0, _spent + correction);
        }
    }

    /// <summary>
    /// Rolls the day over. Called under the lock by every operation rather than on a
    /// timer, so there is no background work to fail silently and no window in which
    /// yesterday's spend blocks today's first request.
    /// </summary>
    private void Roll()
    {
        var today = DateOnly.FromDateTime(timeProvider.GetUtcNow().UtcDateTime);

        if (_day != today)
        {
            _day = today;
            _spent = 0;
        }
    }
}
