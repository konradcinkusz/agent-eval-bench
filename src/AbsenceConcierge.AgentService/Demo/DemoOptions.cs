namespace AbsenceConcierge.AgentService.Demo;

/// <summary>
/// The public demo's ceilings.
///
/// <para>
/// This exists because the demo is the one part of this repository a stranger can
/// spend money on. Everything else runs on fixtures; the live composer runs on a
/// paid model, on an endpoint anybody can reach, with no account and no sign-in. So
/// the shape of the feature is: <b>locked by default, bounded when unlocked, and
/// honest about which of the two it is in.</b>
/// </para>
/// <para>
/// <b>An unset <see cref="AccessCode"/> means live mode is unavailable</b> — not
/// that it is open. That direction matters more than it looks: a missing secret is
/// the normal state of a fork, a preview environment and a fresh clone, and a
/// default that fails open turns every one of those into an unmetered spend.
/// </para>
/// </summary>
public sealed class DemoOptions
{
    public const string SectionName = "Demo";

    /// <summary>
    /// The code a visitor supplies to unlock the live composer, from an environment
    /// secret. Never committed, never logged, and compared in fixed time.
    ///
    /// <para>
    /// It is not authentication and is not described as any. It is a spend control:
    /// it says "somebody I gave this to is asking", which is the whole property the
    /// budget below needs in order to mean anything.
    /// </para>
    /// </summary>
    public string? AccessCode { get; set; }

    /// <summary>
    /// Output tokens the live composer may spend per UTC day, across every visitor.
    ///
    /// <para>
    /// A hard ceiling rather than an alert. The failure this prevents is the one
    /// that only ever shows up on a bill, and the degraded state — deterministic
    /// prose — is a state this service is happy in, so there is no reason to prefer
    /// spending over stopping.
    /// </para>
    /// </summary>
    public int DailyOutputTokenBudget { get; set; } = 20_000;

    /// <summary>
    /// The per-reply ceiling. A reply is two or three sentences; this is sized for
    /// that, not for the model's context window.
    /// </summary>
    public int MaxOutputTokensPerReply { get; set; } = 300;

    /// <summary>Turns per IP per minute. Applies whether or not live mode is unlocked.</summary>
    public int RequestsPerMinutePerClient { get; set; } = 20;
}
