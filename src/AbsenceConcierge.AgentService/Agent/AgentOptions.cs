namespace AbsenceConcierge.AgentService.Agent;

public sealed class AgentOptions
{
    public const string SectionName = "Agent";

    /// <summary>
    /// The IANA zone the acting employee is in. Dates resolve here, never in UTC and
    /// never in the host's zone — a CI runner set to UTC never sees a Madrid
    /// daylight-saving transition, which would make <c>amb-004</c> undetectable
    /// rather than merely undetected.
    /// </summary>
    public string Timezone { get; set; } = "Europe/Madrid";

    /// <summary>
    /// The hard stop on steps per turn. C-4 requires the loop to terminate by
    /// decision and never by exhaustion; this is the backstop that makes reaching it
    /// a recorded, assertable failure instead of an unbounded loop.
    /// </summary>
    public int MaxSteps { get; set; } = 32;

    /// <summary>
    /// Attempts per <b>read</b> tool per turn (SPEC §7 rule 3). Writes get one, and
    /// that is not configurable: "at most two" would permit two <c>request_time_off</c>
    /// spans against one confirmation, which books two holidays and breaks C-6.
    /// </summary>
    public int MaxReadAttempts { get; set; } = 2;

    /// <summary>
    /// The leave-type names to prefer, in order, when the user asked for time off
    /// without naming a reason. Configuration rather than a constant because it is a
    /// policy about a company's leave catalogue, not a fact about the agent.
    /// </summary>
    public IList<string> DefaultLeaveTypePreference { get; } = ["vacation", "annual leave", "paid time off"];
}
