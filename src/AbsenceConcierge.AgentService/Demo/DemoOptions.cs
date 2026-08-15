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

    /// <summary>
    /// Whether live replies are open to visitors who supply no code at all.
    ///
    /// <para>
    /// <b>False by default, and the default is the security property.</b> A fork, a
    /// preview environment and a fresh clone all run with this unset, and all of
    /// them stay locked. Setting it to true is a decision a deployment makes in its
    /// own config — the public demo does, because a demo behind a code is a demo
    /// most visitors never see working — and the spend it opens is bounded twice:
    /// per client by <see cref="LiveTurnsPerClientPerDay"/>, and in total by
    /// <see cref="DailyOutputTokenBudget"/>, which no number of clients moves.
    /// </para>
    /// </summary>
    public bool AllowLiveWithoutCode { get; set; }

    /// <summary>
    /// Live-composed turns one client gets per UTC day, when live mode is open
    /// (<see cref="AllowLiveWithoutCode"/>). A visitor past it keeps the full demo
    /// on the deterministic composer; a visitor with the access code is never
    /// subject to it. Sized so one curious visitor sees plenty and one script
    /// cannot drain the shared budget alone.
    /// </summary>
    public int LiveTurnsPerClientPerDay { get; set; } = 25;

    /// <summary>
    /// Conversations held in memory at once. Past it, the least recently used is
    /// evicted — a bound, because every conversation id a stranger invents is a
    /// dictionary entry this process keeps until it dies, and an unbounded map on
    /// a public endpoint is a memory exhaustion nobody has to be clever to cause.
    /// </summary>
    public int MaxConversations { get; set; } = 10_000;

    /// <summary>
    /// Turns one conversation may carry. A human books leave in a handful; past
    /// this the endpoint answers 429 and suggests a fresh conversation. Bounds the
    /// per-conversation state (retrieved leave-type ids accumulate per turn).
    /// </summary>
    public int MaxTurnsPerConversation { get; set; } = 60;

    /// <summary>
    /// The longest conversation id accepted. The id is a client-invented string
    /// used as a dictionary key; without a bound it is the cheapest way to grow
    /// this process's memory from a shell one-liner.
    /// </summary>
    public int MaxConversationIdLength { get; set; } = 128;

    /// <summary>
    /// The largest request body accepted, in bytes. The turn payload is three
    /// short strings; a kilobyte of message and a hundred or so of envelope. The
    /// framework default is thirty megabytes, which on a 512 MB machine is not a
    /// limit, it is an invitation.
    /// </summary>
    public long MaxRequestBodyBytes { get; set; } = 32 * 1024;

    /// <summary>
    /// Requests in flight at once, across every route but the health probes. A
    /// backstop against a burst holding the whole machine, sized far above what
    /// the page generates and far below what would matter to it.
    /// </summary>
    public int MaxConcurrentRequests { get; set; } = 64;

    /// <summary>
    /// Whether to read the client address from the platform's forwarded header
    /// (<c>Fly-Client-IP</c>) rather than the socket. On Fly every socket peer is
    /// the platform's proxy, so without this every visitor shares one rate-limit
    /// bucket — the corporate-NAT collapse of SERVICE-API-PATTERNS.md §1, applied
    /// to everyone at once. Off by default because trusting a client-settable
    /// header when there is no proxy in front is worse: it hands every visitor a
    /// bucket of their choosing. The Fly config sets it; nothing else should.
    /// </summary>
    public bool TrustProxyClientIpHeader { get; set; }
}
