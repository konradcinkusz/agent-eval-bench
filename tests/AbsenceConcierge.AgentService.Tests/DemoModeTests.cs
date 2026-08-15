using AbsenceConcierge.AgentService.Agent.Llm;
using AbsenceConcierge.AgentService.Demo;
using Microsoft.Extensions.Options;

namespace AbsenceConcierge.AgentService.Tests;

/// <summary>A provider that answers whatever a test hands it, and counts the calls.</summary>
public sealed class FakeLlmProvider(Func<LlmRequest, LlmResponse> answer) : ILlmProvider
{
    public FakeLlmProvider(string text, int outputTokens = 20)
        : this(_ => new LlmResponse(text, "fake-small", 100, outputTokens, "stop"))
    {
    }

    public string Name => "fake";

    public string ConfiguredModel => "fake-small";

    public int Calls { get; private set; }

    public LlmRequest? LastRequest { get; private set; }

    public ValueTask<LlmResponse> CompleteAsync(
        LlmRequest request,
        CancellationToken cancellationToken = default)
    {
        Calls++;
        LastRequest = request;
        return ValueTask.FromResult(answer(request));
    }
}

/// <summary>
/// The public demo's ceilings.
///
/// <para>
/// This is the only part of the repository a stranger can spend money on, so the
/// tests are about the three independent conditions that gate that spend and about
/// each of them failing closed. The interesting cases are the ones where something
/// is <em>missing</em>: a fork with no secret, a deployment with a model and no
/// code, a day whose budget is gone. Each has to land on "not live", and each has to
/// say something different about why.
/// </para>
/// </summary>
public sealed class DemoModeTests
{
    private static readonly DateTimeOffset Morning = new(2026, 8, 15, 9, 0, 0, TimeSpan.Zero);

    // ── The budget ──────────────────────────────────────────────────────────────

    [Fact]
    public void A_reservation_that_would_exceed_the_day_is_refused_rather_than_trimmed()
    {
        // Refused, not partially granted. A reply generated against a trimmed
        // ceiling is a reply cut off mid-sentence, and the degraded state — the
        // deterministic composer — is one this service is perfectly happy in.
        var budget = Budget(dailyBudget: 100);

        Assert.True(budget.TryReserve(60));
        Assert.False(budget.TryReserve(60));
        Assert.Equal(40, budget.State.Remaining);
    }

    [Fact]
    public void Settling_returns_the_headroom_a_reply_did_not_use()
    {
        var budget = Budget(dailyBudget: 1000);

        budget.TryReserve(300);
        budget.Settle(reserved: 300, actualOutputTokens: 42);

        Assert.Equal(958, budget.State.Remaining);
    }

    [Fact]
    public void A_provider_reporting_more_than_it_was_allowed_cannot_credit_the_budget()
    {
        // The numbers come from a remote system. A provider that reported a negative
        // or absurd output count must not be able to hand tokens back.
        var budget = Budget(dailyBudget: 1000);

        budget.TryReserve(300);
        budget.Settle(reserved: 300, actualOutputTokens: 99_999);

        Assert.Equal(700, budget.State.Remaining);
    }

    [Fact]
    public void The_ledger_rolls_over_at_utc_midnight()
    {
        var clock = new MovableTimeProvider(Morning);
        var budget = Budget(dailyBudget: 100, clock);

        Assert.True(budget.TryReserve(100));
        Assert.True(budget.State.Exhausted);

        clock.Advance(TimeSpan.FromHours(16));

        Assert.False(budget.State.Exhausted);
        Assert.Equal(100, budget.State.Remaining);
    }

    // ── The four ways to be not-live ────────────────────────────────────────────

    [Fact]
    public void With_no_model_configured_live_mode_is_not_merely_locked_but_absent()
    {
        var access = new DemoAccess(Options("the-code"), Budget(1000), provider: null);

        var status = access.Evaluate("the-code");

        Assert.False(status.Live);
        Assert.Null(status.Remaining);
    }

    [Fact]
    public void A_deployment_with_a_model_and_no_access_code_is_closed_not_open()
    {
        // The case this whole design is arranged around. A missing secret is the
        // normal state of a fork and a preview environment, and a default that failed
        // open would make every one of them an unmetered spend.
        var access = new DemoAccess(Options(accessCode: null), Budget(1000), new FakeLlmProvider("hello"));

        Assert.False(access.Evaluate("anything").Live);
        Assert.False(access.Status().Live);
    }

    [Fact]
    public void The_wrong_code_and_no_code_are_both_locked_and_say_so_differently_from_an_empty_budget()
    {
        var access = new DemoAccess(Options("the-code"), Budget(1000), new FakeLlmProvider("hello"));

        var wrong = access.Evaluate("not-the-code");
        var exhausted = new DemoAccess(Options("the-code"), Spent(), new FakeLlmProvider("hello"))
            .Evaluate("the-code");

        Assert.False(wrong.Live);
        Assert.False(exhausted.Live);

        // A misconfigured deployment needs a fix and an exhausted one needs tomorrow.
        // One "unavailable" for both would make them indistinguishable on the page.
        Assert.NotEqual(wrong.Reason, exhausted.Reason);
        Assert.Equal(0, exhausted.Remaining);
    }

    [Fact]
    public void The_right_code_with_budget_left_is_live()
    {
        var access = new DemoAccess(Options("the-code"), Budget(1000), new FakeLlmProvider("hello"));

        var status = access.Evaluate("the-code");

        Assert.True(status.Live);
        Assert.Equal(1000, status.Remaining);
    }

    // ── Helpers ─────────────────────────────────────────────────────────────────

    private static IOptions<DemoOptions> Options(string? accessCode) =>
        Microsoft.Extensions.Options.Options.Create(new DemoOptions { AccessCode = accessCode });

    private static DemoBudget Budget(int dailyBudget, TimeProvider? clock = null) =>
        new(
            clock ?? new FixedTimeProvider(Morning),
            Microsoft.Extensions.Options.Options.Create(new DemoOptions { DailyOutputTokenBudget = dailyBudget }));

    private static DemoBudget Spent()
    {
        var budget = Budget(50);
        budget.TryReserve(50);
        return budget;
    }
}

/// <summary>A clock a test can move, for the one behaviour that is about time passing.</summary>
public sealed class MovableTimeProvider(DateTimeOffset start) : TimeProvider
{
    private DateTimeOffset _now = start;

    public override DateTimeOffset GetUtcNow() => _now;

    public void Advance(TimeSpan by) => _now = _now.Add(by);
}
