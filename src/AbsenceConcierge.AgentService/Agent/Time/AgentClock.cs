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

    /// <summary>
    /// The configured zone, or a loud failure. Shared rather than private to the
    /// orchestrator because the tool boundary needs the same answer: it enforces the
    /// past-date rule a second time, and two layers disagreeing about what day it is
    /// on one request is the quiet frame error this class exists to prevent.
    /// </summary>
    public static TimeZoneInfo ZoneFor(string id)
    {
        try
        {
            return TimeZoneInfo.FindSystemTimeZoneById(id);
        }
        catch (TimeZoneNotFoundException)
        {
            // Deliberately loud and deliberately fatal. Falling back to UTC would
            // resolve every date in the wrong frame while every test still passed,
            // which is the exact defect InvariantGlobalization=false exists to
            // prevent (Directory.Build.props).
            throw new InvalidOperationException(
                $"Timezone '{id}' is not available on this machine. The container "
                + "must carry tzdata; see Directory.Build.props for why globalization is not trimmed.");
        }
    }
}
