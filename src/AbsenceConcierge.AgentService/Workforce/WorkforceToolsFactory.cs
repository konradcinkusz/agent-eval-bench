using AbsenceConcierge.AgentService.Workforce.Confirmation;
using AbsenceConcierge.AgentService.Workforce.Fixtures;
using AbsenceConcierge.AgentService.Workforce.Mock;

namespace AbsenceConcierge.AgentService.Workforce;

/// <summary>
/// Assembles the tool chain: the backend, an optional layer over it, and the
/// instrumentation that every implementation is seen through.
///
/// <para>
/// It exists so there is <b>one</b> assembly order. The service builds this chain
/// at startup and the eval harness builds it per scenario, and if those two drifted
/// the suite would be measuring a shape the deployed service does not have — which
/// is the same failure the mock-in-the-service rule exists to prevent, one level up.
/// </para>
/// <para>
/// The order is load-bearing: fault injection goes <em>beneath</em> the
/// instrumentation, so an injected failure still produces one span with its attempt
/// events, which is what the degradation scenarios read.
/// </para>
/// </summary>
public static class WorkforceToolsFactory
{
    public static IWorkforceTools Build(
        WorkforceWorld world,
        IConfirmationTokenStore tokens,
        TimeProvider timeProvider,
        int maxReadAttempts,
        Func<IWorkforceTools, IWorkforceTools>? decorate = null)
    {
        IWorkforceTools tools = new MockWorkforceTools(world, tokens, timeProvider);

        if (decorate is not null)
        {
            tools = decorate(tools);
        }

        return new InstrumentedWorkforceTools(tools, new ToolAttemptPolicy(maxReadAttempts));
    }
}
