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
/// Four ways to be not-live, and the page distinguishes all four: no model
/// configured, no access code configured, the wrong code supplied, today's budget
/// spent. A single "unavailable" would make a misconfigured deployment
/// indistinguishable from an exhausted one, and the first needs a fix while the
/// second needs tomorrow.
/// </para>
/// </summary>
public sealed class DemoAccess(
    IOptions<DemoOptions> options,
    IDemoBudget budget,
    ILlmProvider? provider = null)
{
    private readonly DemoOptions _options = options.Value;

    /// <summary>
    /// The status with no code supplied — what the page asks for on load, so it can
    /// say whether unlocking is even possible before anybody types anything.
    /// </summary>
    public DemoStatus Status() => Evaluate(null);

    public DemoStatus Evaluate(string? suppliedCode)
    {
        if (provider is null)
        {
            return new DemoStatus(
                false,
                "No model is configured. Replies are written by the deterministic composer.",
                null);
        }

        if (string.IsNullOrWhiteSpace(_options.AccessCode))
        {
            // A deployment with a model but no code is not an open deployment. This
            // is the fork and preview-environment case, and failing closed is the
            // whole reason the check is written this way round.
            return new DemoStatus(
                false,
                "Live replies are not enabled on this deployment.",
                null);
        }

        var state = budget.State;

        if (string.IsNullOrEmpty(suppliedCode) || !Matches(suppliedCode, _options.AccessCode))
        {
            return new DemoStatus(
                false,
                "Live replies need an access code. Everything else on this page works without one.",
                state.Remaining);
        }

        if (state.Exhausted)
        {
            return new DemoStatus(
                false,
                "Today's model budget is spent. Replies are written by the deterministic composer until midnight UTC.",
                0);
        }

        return new DemoStatus(true, "Replies on this turn are written by a model.", state.Remaining);
    }

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
