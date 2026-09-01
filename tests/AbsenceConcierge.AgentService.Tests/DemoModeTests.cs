using AbsenceConcierge.AgentService.Agent;
using AbsenceConcierge.AgentService.Extensions;
using AbsenceConcierge.AgentService.Workforce;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
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

    // ── The ways to be not-live ─────────────────────────────────────────────────

    [Fact]
    public void With_no_model_configured_live_mode_is_not_merely_locked_but_absent()
    {
        var access = Access(Options("the-code"), Budget(1000), provider: null);

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
        var access = Access(Options(accessCode: null), Budget(1000), new FakeLlmProvider("hello"));

        Assert.False(access.Evaluate("anything").Live);
        Assert.False(access.Status().Live);
        Assert.False(access.BeginTurn("anything", "203.0.113.7").Live);
    }

    [Fact]
    public void The_wrong_code_and_no_code_are_both_locked_and_say_so_differently_from_an_empty_budget()
    {
        var access = Access(Options("the-code"), Budget(1000), new FakeLlmProvider("hello"));

        var wrong = access.Evaluate("not-the-code");
        var exhausted = Access(Options("the-code"), Spent(), new FakeLlmProvider("hello"))
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
        var access = Access(Options("the-code"), Budget(1000), new FakeLlmProvider("hello"));

        var status = access.Evaluate("the-code");

        Assert.True(status.Live);
        Assert.Equal(1000, status.Remaining);
    }

    // ── Open access: live without a code, bounded per client ────────────────────

    [Fact]
    public void Open_access_is_live_without_a_code_and_consumes_the_clients_allowance()
    {
        var access = Access(Open(turnsPerClient: 2), Budget(1000), new FakeLlmProvider("hello"));

        Assert.True(access.BeginTurn(null, "203.0.113.7").Live);
        Assert.True(access.BeginTurn(null, "203.0.113.7").Live);

        var spent = access.BeginTurn(null, "203.0.113.7");

        Assert.False(spent.Live);
        Assert.Contains("allowance", spent.Reason, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void One_clients_spent_allowance_does_not_touch_another_clients()
    {
        var access = Access(Open(turnsPerClient: 1), Budget(1000), new FakeLlmProvider("hello"));

        Assert.True(access.BeginTurn(null, "203.0.113.7").Live);
        Assert.False(access.BeginTurn(null, "203.0.113.7").Live);

        Assert.True(access.BeginTurn(null, "198.51.100.9").Live);
    }

    [Fact]
    public void The_status_probe_never_consumes_the_allowance()
    {
        var access = Access(Open(turnsPerClient: 1), Budget(1000), new FakeLlmProvider("hello"));

        for (var i = 0; i < 5; i++)
        {
            Assert.True(access.Evaluate(null, "203.0.113.7").Live);
        }

        // The allowance is intact: the first real turn is still live.
        Assert.True(access.BeginTurn(null, "203.0.113.7").Live);
    }

    [Fact]
    public void The_shared_budget_still_wins_over_an_open_allowance()
    {
        // The allowance is the fairness rule; the budget is the bill's ceiling. A
        // hundred clients with allowance left must all go deterministic the moment
        // the shared budget is gone.
        var access = Access(Open(turnsPerClient: 100), Spent(), new FakeLlmProvider("hello"));

        var status = access.BeginTurn(null, "203.0.113.7");

        Assert.False(status.Live);
        Assert.Equal(0, status.Remaining);
    }

    [Fact]
    public void An_unidentifiable_client_gets_no_unmetered_live_turn()
    {
        var access = Access(Open(turnsPerClient: 5), Budget(1000), new FakeLlmProvider("hello"));

        Assert.False(access.BeginTurn(null, clientKey: null).Live);
        Assert.False(access.BeginTurn(null, clientKey: string.Empty).Live);
    }

    [Fact]
    public void A_code_holder_is_not_subject_to_the_open_allowance()
    {
        var options = Open(turnsPerClient: 1, accessCode: "the-code");
        var access = Access(options, Budget(1000), new FakeLlmProvider("hello"));

        Assert.True(access.BeginTurn(null, "203.0.113.7").Live);
        Assert.False(access.BeginTurn(null, "203.0.113.7").Live);

        // Same client, with the code: the allowance no longer applies.
        Assert.True(access.BeginTurn("the-code", "203.0.113.7").Live);
    }

    [Fact]
    public void The_client_allowance_rolls_over_at_utc_midnight()
    {
        var clock = new MovableTimeProvider(Morning);
        var options = Open(turnsPerClient: 1);
        var access = new DemoAccess(
            options,
            Budget(1000, clock),
            new DemoClientQuota(clock, options),
            new FakeLlmProvider("hello"));

        Assert.True(access.BeginTurn(null, "203.0.113.7").Live);
        Assert.False(access.BeginTurn(null, "203.0.113.7").Live);

        clock.Advance(TimeSpan.FromHours(16));

        Assert.True(access.BeginTurn(null, "203.0.113.7").Live);
    }

    // ── The conversation store's ceiling ────────────────────────────────────────

    [Fact]
    public void Past_the_conversation_cap_the_least_recently_touched_is_evicted()
    {
        var store = new InMemoryAgentConversationStore(
            Microsoft.Extensions.Options.Options.Create(new DemoOptions { MaxConversations = 2 }));

        var first = store.GetOrCreate("first");
        _ = store.GetOrCreate("second");

        // Touch the oldest so "least recently used" and "first created" diverge —
        // an eviction keyed on creation order would fail here.
        _ = store.GetOrCreate("first");
        _ = store.GetOrCreate("third");

        Assert.Null(store.Find("second"));
        Assert.Same(first, store.Find("first"));
        Assert.NotNull(store.Find("third"));
    }

    [Fact]
    public void Finding_a_conversation_never_creates_one()
    {
        var store = new InMemoryAgentConversationStore(
            Microsoft.Extensions.Options.Options.Create(new DemoOptions()));

        Assert.Null(store.Find("never-started"));
        Assert.Null(store.Find("never-started"));
    }

    // ── Helpers ─────────────────────────────────────────────────────────────────

    private static DemoAccess Access(IOptions<DemoOptions> options, DemoBudget budget, ILlmProvider? provider) =>
        new(options, budget, new DemoClientQuota(new FixedTimeProvider(Morning), options), provider);

    private static IOptions<DemoOptions> Options(string? accessCode) =>
        Microsoft.Extensions.Options.Options.Create(new DemoOptions { AccessCode = accessCode });

    private static IOptions<DemoOptions> Open(int turnsPerClient, string? accessCode = null) =>
        Microsoft.Extensions.Options.Options.Create(new DemoOptions
        {
            AccessCode = accessCode,
            AllowLiveWithoutCode = true,
            LiveTurnsPerClientPerDay = turnsPerClient,
        });

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

    // ── The fault seam ──────────────────────────────────────────────────────────

    [Theory]
    [InlineData(null, true)]
    [InlineData("http_500", false)]
    public async Task A_tool_fails_only_when_a_fault_is_configured_for_it(string? outcome, bool expectSuccess)
    {
        // Asserted through behaviour, not through the resolved type. The fault
        // decorator sits BENEATH the instrumentation one — WorkforceToolsFactory
        // wraps whatever it decorates — so `IsNotType<FaultInjectingWorkforceTools>`
        // is trivially true whether the seam is on or off, and a test asserting it
        // passes with the gate forced open. Calling the tool is the only question
        // that has a different answer.
        var settings = new Dictionary<string, string?>
        {
            ["WorkforceTools:Fixture"] = "meridian-labs",
            ["Demo:MaxConfirmationTokens"] = "8",
        };

        if (outcome is not null)
        {
            settings["WorkforceTools:Faults:list_leaves:Outcome"] = outcome;
        }

        var services = new ServiceCollection()
            .AddLogging()
            .AddWorkforceTools(new ConfigurationBuilder().AddInMemoryCollection(settings).Build());

        services.Configure<AgentOptions>(_ => { });
        services.Configure<DemoOptions>(_ => { });

        using var provider = services.BuildServiceProvider();

        var result = await provider.GetRequiredService<IWorkforceTools>()
            .ListLeavesAsync(TestContext.Current.CancellationToken);

        Assert.Equal(expectSuccess, result.IsSuccess);
    }

    [Fact]
    public void The_public_deployment_carries_no_fault_configuration()
    {
        // Unreachable rather than switched off, and this is the assertion that
        // keeps it that way. A future edit that adds a fault to the demo would
        // otherwise be a one-line change with no test between it and production.
        var config = File.ReadAllText(DemoFlyToml());

        // Only the [env] block matters, and the search is anchored to the start of
        // a line: the header comment above it discusses `[env]` in prose, and
        // names this very setting on purpose to explain why it is absent — so an
        // unanchored search finds the explanation and fails on it.
        var section = config.IndexOf("\n[env]", StringComparison.Ordinal);
        Assert.True(section >= 0, "demo.fly.toml has no [env] section.");

        var env = config[section..];

        // The configuration key, not the word. "Faults" case-insensitively is also
        // inside "defaults", which this file says twice — the same substring trap
        // that let "maybe" arm the date parser's bare-number gate.
        Assert.DoesNotContain("WorkforceTools__Faults", env, StringComparison.Ordinal);
    }

    private static string DemoFlyToml()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);

        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "AbsenceConcierge.slnx")))
        {
            directory = directory.Parent;
        }

        Assert.NotNull(directory);
        return Path.Combine(directory!.FullName, "flyio", "demo.fly.toml");
    }
}

/// <summary>A clock a test can move, for the one behaviour that is about time passing.</summary>
public sealed class MovableTimeProvider(DateTimeOffset start) : TimeProvider
{
    private DateTimeOffset _now = start;

    public override DateTimeOffset GetUtcNow() => _now;

    public void Advance(TimeSpan by) => _now = _now.Add(by);

}
