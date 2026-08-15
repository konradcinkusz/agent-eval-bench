using System.Security.Cryptography;
using System.Text;
using AbsenceConcierge.AgentService.Agent.Llm;
using Microsoft.Extensions.Options;

namespace AbsenceConcierge.AgentService.Demo;

/// <param name="Live">Whether this turn's reply may be written by a model.</param>
/// <param name="Reason">
/// Why, in words the page shows the visitor. Every not-live state has one, because
/// "the model is off" and "the model is off because today's budget is spent" are
/// different facts and the second is the one that explains the banner.
/// </param>
/// <param name="Remaining">Output tokens left today, or <c>null</c> when live mode is not configured at all.</param>
public sealed record DemoStatus(bool Live, string Reason, int? Remaining);

/// <summary>
/// Decides whether a turn gets the live composer.
///
/// <para>
/// Five ways to be not-live, and the page distinguishes all five: no model
/// configured, live mode not enabled, the wrong code supplied, today's shared
/// budget spent, this client's daily allowance spent. A single "unavailable" would
/// make a misconfigured deployment indistinguishable from an exhausted one, and
/// the first needs a fix while the second needs tomorrow.
/// </para>
/// <para>
/// <b>Two ways to be live.</b> The original: a code, configured from a secret and
/// supplied by the visitor — a spend control, not authentication. And the open
/// one: <see cref="DemoOptions.AllowLiveWithoutCode"/> set on a deployment that
/// chose it, bounded per client by <see cref="IDemoClientQuota"/> and in total by
/// the budget. Both fail closed: a fork with neither the flag nor a code stays
/// locked, which is the direction that matters (P8).
/// </para>
/// </summary>
public sealed class DemoAccess(
    IOptions<DemoOptions> options,
    IDemoBudget budget,
    IDemoClientQuota quota,
    ILlmProvider? provider = null)
{
    private readonly DemoOptions _options = options.Value;

    /// <summary>
    /// The status with no code supplied — what the page asks for on load, so it can
    /// say whether unlocking is even possible before anybody types anything.
    /// </summary>
    public DemoStatus Status() => Evaluate(null, clientKey: null);

    /// <summary>
    /// A look, not a spend: nothing is consumed, so the status route can be polled
    /// without eating anybody's allowance.
    /// </summary>
    public DemoStatus Evaluate(string? suppliedCode, string? clientKey = null) =>
        Decide(suppliedCode, clientKey, consume: false);

    /// <summary>
    /// The decision for a turn that is about to run. In open access this consumes
    /// one unit of the client's daily allowance — here rather than in the composer,
    /// because the allowance is a decision about who gets the model, and decisions
    /// about the turn are made before it starts.
    /// </summary>
    public DemoStatus BeginTurn(string? suppliedCode, string? clientKey) =>
        Decide(suppliedCode, clientKey, consume: true);

    private DemoStatus Decide(string? suppliedCode, string? clientKey, bool consume)
    {
        if (provider is null)
        {
            return new DemoStatus(
                false,
                "No model is configured. Replies are written by the deterministic composer.",
                null);
        }

        var state = budget.State;
        var hasCode = !string.IsNullOrWhiteSpace(_options.AccessCode);

        if (hasCode && !string.IsNullOrEmpty(suppliedCode) && Matches(suppliedCode, _options.AccessCode!))
        {
            // A code holder is somebody the operator gave the code to. The shared
            // budget still applies — it is the ceiling on the bill — but the
            // per-client fairness rule does not.
            return state.Exhausted ? BudgetSpent() : Live("Replies on this turn are written by a model.", state);
        }

        if (_options.AllowLiveWithoutCode)
        {
            if (state.Exhausted)
            {
                return BudgetSpent();
            }

            if (string.IsNullOrEmpty(clientKey))
            {
                // No key means no way to meter this client — the status probe, or a
                // deployment that could not resolve an address. Report what a turn
                // would get rather than granting an unmetered one.
                return consume
                    ? new DemoStatus(
                        false,
                        "Live replies are open here, but this client could not be identified for metering. "
                        + "Replies are written by the deterministic composer.",
                        state.Remaining)
                    : Live("Live replies are open on this deployment, within a daily budget.", state);
            }

            if (consume ? !quota.TryConsume(clientKey) : quota.Remaining(clientKey) == 0)
            {
                return new DemoStatus(
                    false,
                    "This address has used today's live-reply allowance. Replies are written by the "
                    + "deterministic composer until midnight UTC — everything else works unchanged.",
                    state.Remaining);
            }

            return Live("Replies on this turn are written by a model, within today's open allowance.", state);
        }

        if (!hasCode)
        {
            // A deployment with a model, no code and no open-access flag is not an
            // open deployment. This is the fork and preview-environment case, and
            // failing closed is the whole reason the check is written this way round.
            return new DemoStatus(
                false,
                "Live replies are not enabled on this deployment.",
                null);
        }

        return new DemoStatus(
            false,
            "Live replies need an access code. Everything else on this page works without one.",
            state.Remaining);
    }

    private static DemoStatus Live(string reason, BudgetState state) =>
        new(true, reason, state.Remaining);

    private static DemoStatus BudgetSpent() =>
        new(
            false,
            "Today's model budget is spent. Replies are written by the deterministic composer until midnight UTC.",
            0);

    /// <summary>
    /// Fixed-time comparison over hashes.
    ///
    /// <para>
    /// Hashed first so the comparison is over two equal-length inputs — comparing raw
    /// strings in fixed time still leaks their length, and a length is a meaningful
    /// head start against a short code. This is a low-value secret guarding a spend
    /// ceiling rather than data, and it is still not worth writing the version that
    /// returns early on the first differing byte.
    /// </para>
    /// </summary>
    private static bool Matches(string supplied, string expected) =>
        CryptographicOperations.FixedTimeEquals(
            SHA256.HashData(Encoding.UTF8.GetBytes(supplied)),
            SHA256.HashData(Encoding.UTF8.GetBytes(expected)));
}
