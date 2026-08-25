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
    /// <param name="zone">
    /// The actor's timezone — the same one <see cref="Agent.Time.AgentClock"/> is
    /// given. The mock enforces the past-date rule a second time, and a second layer
    /// computing "today" in a different frame from the first is two answers to one
    /// question on one request.
    /// </param>
    public static IWorkforceTools Build(
        WorkforceWorld world,
        IConfirmationTokenStore tokens,
        TimeProvider timeProvider,
        TimeZoneInfo zone,
        int maxReadAttempts,
        Func<IWorkforceTools, IWorkforceTools>? decorate = null) =>
        Instrument(new MockWorkforceTools(world, tokens, timeProvider, zone), maxReadAttempts, decorate);

    /// <summary>
    /// The same chain over a backend that is not the mock.
    ///
    /// <para>
    /// The MCP adapter goes through here rather than constructing its own decorator,
    /// so the two modes are not merely documented as producing the same trace shape —
    /// they produce it by running the same three lines. A scenario that passes on the
    /// mock is then evidence about the span shape of the integration, which is the
    /// only claim this repository can make about a mode it has no server to test.
    /// </para>
    /// </summary>
    public static IWorkforceTools Instrument(
        IWorkforceTools backend,
        int maxReadAttempts,
        Func<IWorkforceTools, IWorkforceTools>? decorate = null)
    {
        var tools = decorate is null ? backend : decorate(backend);

        return new InstrumentedWorkforceTools(tools, new ToolAttemptPolicy(maxReadAttempts));
    }
}
