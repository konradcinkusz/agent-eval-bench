using AbsenceConcierge.AgentService.Agent;
using AbsenceConcierge.AgentService.Agent.Steps;
using AbsenceConcierge.AgentService.Extensions;
using AbsenceConcierge.AgentService.Telemetry;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace AbsenceConcierge.AgentService.Tests;

/// <summary>
/// SPEC §2.3's precedence, tested on its own.
///
/// It is a contract, not a convention, so it is implemented in one class and checked
/// here rather than inferred from whichever step happened to write the attribute last.
/// </summary>
public sealed class TurnOutcomeRecorderTests
{
    [Fact]
    public void A_turn_that_recorded_nothing_completed()
    {
        Assert.Equal(AgentDiagnostics.TurnOutcomes.Completed, new TurnOutcomeRecorder().Resolve());
    }

    [Theory]
    [InlineData(AgentDiagnostics.TurnOutcomes.Refused, AgentDiagnostics.TurnOutcomes.Degraded, AgentDiagnostics.TurnOutcomes.Refused)]
    [InlineData(AgentDiagnostics.TurnOutcomes.Degraded, AgentDiagnostics.TurnOutcomes.ConfirmationPending, AgentDiagnostics.TurnOutcomes.Degraded)]
    [InlineData(AgentDiagnostics.TurnOutcomes.ClarificationRequested, AgentDiagnostics.TurnOutcomes.Completed, AgentDiagnostics.TurnOutcomes.ClarificationRequested)]
    [InlineData(AgentDiagnostics.TurnOutcomes.Cancelled, AgentDiagnostics.TurnOutcomes.Completed, AgentDiagnostics.TurnOutcomes.Cancelled)]
    public void Where_two_apply_the_higher_precedence_wins(string first, string second, string expected)
    {
        var recorder = new TurnOutcomeRecorder();
        recorder.Record(first);
        recorder.Record(second);

        Assert.Equal(expected, recorder.Resolve());
    }

    [Fact]
    public void The_order_of_recording_does_not_change_the_answer()
    {
        // deg-002 is the turn this protects: a draft was shown and a read failed, in
        // that order and in the other. Reporting it as confirmation_pending would
        // hide the half that matters.
        var forwards = new TurnOutcomeRecorder();
        forwards.Record(AgentDiagnostics.TurnOutcomes.ConfirmationPending);
        forwards.Record(AgentDiagnostics.TurnOutcomes.Degraded);

        var backwards = new TurnOutcomeRecorder();
        backwards.Record(AgentDiagnostics.TurnOutcomes.Degraded);
        backwards.Record(AgentDiagnostics.TurnOutcomes.ConfirmationPending);

        Assert.Equal(AgentDiagnostics.TurnOutcomes.Degraded, forwards.Resolve());
        Assert.Equal(AgentDiagnostics.TurnOutcomes.Degraded, backwards.Resolve());
    }

    [Fact]
    public void An_outcome_outside_the_closed_set_is_rejected_rather_than_recorded()
    {
        // A turn state that is not in SPEC §2.3 is a specification change nobody
        // made. Failing here is cheaper than a scenario asserting an outcome the
        // harness has never seen.
        var recorder = new TurnOutcomeRecorder();

        Assert.Throws<ArgumentOutOfRangeException>(() => recorder.Record("mostly_fine"));
    }
}

/// <summary>
/// The pipeline's order is the specification (SPEC §4's constraints are properties
/// of it), so the order the container registers is checked against the order the
/// tests exercise. Without this, the two could drift and every end-to-end test here
/// would keep passing against a pipeline the deployed service does not have.
/// </summary>
public sealed class AgentRegistrationTests
{
    [Fact]
    public void The_container_registers_the_steps_in_the_order_the_tests_run_them()
    {
        var services = new ServiceCollection();
        services.AddAbsenceConciergeAgent(new ConfigurationBuilder().Build());

        var registered = services
            .Where(descriptor => descriptor.ServiceType == typeof(IAgentStep))
            .Select(descriptor => descriptor.ImplementationType)
            .OfType<Type>()
            .ToList();

        Assert.Equal(AgentHarness.PipelineOrder, registered);
    }

    [Fact]
    public void The_gate_is_registered_before_the_write()
    {
        // C-1 as a property of the list. Stated separately from the equality above
        // because that assertion fails on any reordering, and this one names the
        // reordering that would actually book a holiday nobody approved.
        var order = AgentHarness.PipelineOrder.ToList();
        var gate = order.IndexOf(typeof(ConfirmationGateStep));
        var write = order.IndexOf(typeof(ExecuteWriteStep));

        Assert.True(gate >= 0 && write >= 0);
        Assert.True(gate < write, "The confirmation gate must be registered before the write step.");
    }
}
