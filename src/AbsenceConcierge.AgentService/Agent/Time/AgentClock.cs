namespace AbsenceConcierge.AgentService.Agent.Time;

/// <summary>
/// The agent's sense of "now", and the only place in the agent where an instant
/// becomes a calendar date.
///
/// <para>
/// <c>DateTime.Now</c> does not appear anywhere in this service, and this type is
/// why it does not have to. The instant comes from an injected
/// <see cref="TimeProvider"/> and the zone from configuration, so a scenario can
/// pin both — which is what turns "does this work across a daylight-saving change?"
/// from a thing only a human ever catches (TESTING-STRATEGY.md §7 puts clock and
/// timezone shifts in the manual column) into an assertion that runs on every pull
/// request.
/// </para>
/// <para>
/// A CI runner set to UTC never sees the Europe/Madrid transition at all. Without
/// an injected zone the bug is not merely undetected, it is undetectable, and the
/// suite reports green while the behaviour is wrong for every user in the zone the
/// product ships to.
/// </para>
/// </summary>
public sealed class AgentClock(TimeProvider timeProvider, TimeZoneInfo zone)
{
    public TimeZoneInfo Zone => zone;

    public DateTimeOffset Instant => timeProvider.GetUtcNow();

    /// <summary>The actor's local calendar date. Everything downstream is date arithmetic.</summary>
    public DateOnly Today => DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(Instant, zone).DateTime);
}
