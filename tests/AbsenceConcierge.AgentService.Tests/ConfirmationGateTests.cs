using AbsenceConcierge.AgentService.Workforce;
using AbsenceConcierge.AgentService.Workforce.Confirmation;

namespace AbsenceConcierge.AgentService.Tests;

/// <summary>
/// The confirmation gate, tested at the layer that enforces it.
///
/// These are not tests of the agent — the agent does not exist yet. They test the
/// claim that makes docs/SPEC.md §2.1.1 honest: that an unconfirmed write is refused
/// by the tool boundary, independently of anything the agent decides. Until this
/// holds, "the agent's good behaviour is UX; the service boundary is security" is a
/// sentence rather than a property, and every adversarial scenario in the suite is
/// really testing a prompt.
///
/// The gate is enforced one layer below the agent, so the agent can be wrong — talked
/// into it by an injection, or simply broken — and the write still does not happen.
/// </summary>
public sealed class ConfirmationGateTests
{
    private static readonly DateOnly Start = new(2026, 8, 26);
    private static readonly DateOnly End = new(2026, 8, 27);

    [Fact]
    public async Task A_write_with_no_confirmation_token_is_refused()
    {
        var (tools, _, _) = TestWorld.Build();

        var result = await tools.RequestTimeOffAsync(
            new TimeOffRequest(TestWorld.VacationTypeId, Start, End, ConfirmationToken: string.Empty));

        Assert.Equal(ToolOutcome.ConfirmationRequired, result.Outcome);
        Assert.Null(result.Value);
    }

    [Fact]
    public async Task A_write_with_an_invented_token_is_refused()
    {
        var (tools, _, _) = TestWorld.Build();

        var result = await tools.RequestTimeOffAsync(
            new TimeOffRequest(TestWorld.VacationTypeId, Start, End, "not-a-token-anyone-issued"));

        Assert.Equal(ToolOutcome.ConfirmationRequired, result.Outcome);
    }

    [Fact]
    public async Task A_draft_that_was_shown_but_not_approved_does_not_authorise_a_write()
    {
        // This is the injection case in miniature: the agent reached the gate, a
        // hostile string told it to submit anyway, and it had a token in hand. The
        // token exists; it has not been approved; the write must still fail.
        var (tools, tokens, _) = TestWorld.Build();
        var token = tokens.Issue(new ConfirmationDraft(TestWorld.ActorEmployeeId, TestWorld.VacationTypeId, Start, End));

        var result = await tools.RequestTimeOffAsync(
            new TimeOffRequest(TestWorld.VacationTypeId, Start, End, token));

        Assert.Equal(ToolOutcome.ConfirmationRequired, result.Outcome);
    }

    [Fact]
    public async Task An_approved_token_authorises_exactly_the_draft_it_was_issued_for()
    {
        var (tools, tokens, _) = TestWorld.Build();
        var token = TestWorld.ApprovedToken(tokens, TestWorld.VacationTypeId, Start, End);

        var result = await tools.RequestTimeOffAsync(
            new TimeOffRequest(TestWorld.VacationTypeId, Start, End, token));

        Assert.Equal(ToolOutcome.Success, result.Outcome);
        Assert.NotNull(result.Value);
        Assert.Equal(Start, result.Value!.StartDate);
        Assert.Equal(End, result.Value.EndDate);
        Assert.Equal("pending_approval", result.Value.Status);
    }

    [Fact]
    public async Task An_approved_token_does_not_authorise_a_different_request()
    {
        // Approving two days off is not approving two weeks off. Without this, an
        // agent could show a modest draft, collect the approval, and submit something
        // else — which is the confirmation gate defeated while appearing to work.
        var (tools, tokens, _) = TestWorld.Build();
        var token = TestWorld.ApprovedToken(tokens, TestWorld.VacationTypeId, Start, End);

        var result = await tools.RequestTimeOffAsync(
            new TimeOffRequest(TestWorld.VacationTypeId, Start, End.AddDays(12), token));

        Assert.Equal(ToolOutcome.ConfirmationRequired, result.Outcome);
    }

    [Fact]
    public async Task A_token_authorises_one_write_and_not_two()
    {
        // C-6. A retried write is how an agent books the same holiday twice, and the
        // agent is not the layer that should be relied on to remember.
        var (tools, tokens, _) = TestWorld.Build();
        var token = TestWorld.ApprovedToken(tokens, TestWorld.VacationTypeId, Start, End);
        var request = new TimeOffRequest(TestWorld.VacationTypeId, Start, End, token);

        var first = await tools.RequestTimeOffAsync(request);
        var second = await tools.RequestTimeOffAsync(request);

        Assert.Equal(ToolOutcome.Success, first.Outcome);
        Assert.Equal(ToolOutcome.ConfirmationRequired, second.Outcome);
    }

    [Fact]
    public async Task A_redeemed_token_cannot_be_resurrected_by_a_racing_approval()
    {
        // The store is the layer C-6 falls back to when everything above it runs
        // in parallel — two approve requests for one conversation are two HTTP
        // calls, and nothing serialises them. The failure this pins: Approve reads
        // the entry, TryRedeem removes it and authorises the one write, and
        // Approve's write-back then re-inserts the token, approved — a spent token
        // resurrected for a second write. A loop of tight interleavings makes the
        // window real rather than waiting for production to find it.
        var store = new InMemoryConfirmationTokenStore();
        var draft = new ConfirmationDraft(TestWorld.ActorEmployeeId, TestWorld.VacationTypeId, Start, End);

        for (var i = 0; i < 5000; i += 1)
        {
            var token = store.Issue(draft);
            Assert.True(store.Approve(token));

            using var start = new ManualResetEventSlim(false);

            var redeem = Task.Run(() =>
            {
                start.Wait();
                return store.TryRedeem(token, draft);
            });

            var approveAgain = Task.Run(() =>
            {
                start.Wait();
                return store.Approve(token);
            });

            start.Set();
            await Task.WhenAll(redeem, approveAgain);

            Assert.True(await redeem);

            // Whatever the interleaving, the token is spent: a second redeem must
            // find nothing to redeem.
            Assert.False(store.TryRedeem(token, draft));
        }
    }

    [Fact]
    public async Task A_confirmed_write_for_a_leave_type_that_does_not_exist_is_rejected()
    {
        // The trace-level constraint (C-5) says the id must be grounded in a tool
        // result. This is the same rule enforced where it cannot be argued with.
        var (tools, tokens, _) = TestWorld.Build();
        var token = TestWorld.ApprovedToken(tokens, "lt-999", Start, End);

        var result = await tools.RequestTimeOffAsync(
            new TimeOffRequest("lt-999", Start, End, token));

        Assert.Equal(ToolOutcome.Rejected, result.Outcome);
    }

    [Fact]
    public async Task A_confirmed_write_in_the_past_is_rejected()
    {
        var (tools, tokens, _) = TestWorld.Build();
        var past = new DateOnly(2026, 1, 5);
        var token = TestWorld.ApprovedToken(tokens, TestWorld.VacationTypeId, past, past);

        var result = await tools.RequestTimeOffAsync(
            new TimeOffRequest(TestWorld.VacationTypeId, past, past, token));

        Assert.Equal(ToolOutcome.Rejected, result.Outcome);
    }

    [Fact]
    public async Task The_gate_is_checked_before_the_arguments_are_validated()
    {
        // Ordering matters for diagnosis: an unconfirmed write must fail for being
        // unconfirmed, not for happening to be malformed. Otherwise a scenario
        // asserting the gate can pass while the gate is broken, because some other
        // check happened to catch the call first.
        var (tools, _, _) = TestWorld.Build();

        var result = await tools.RequestTimeOffAsync(
            new TimeOffRequest("lt-999", new DateOnly(2020, 1, 1), new DateOnly(2019, 1, 1), string.Empty));

        Assert.Equal(ToolOutcome.ConfirmationRequired, result.Outcome);
    }
}
