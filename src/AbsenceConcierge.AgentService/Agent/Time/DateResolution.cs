namespace AbsenceConcierge.AgentService.Agent.Time;

/// <summary>
/// Why a date expression could not be turned into a single answer.
///
/// Every value here is a reason to <em>ask</em>, never a reason to guess. The
/// distinction the enum encodes is the one B-12 is about: an agent that resolves an
/// ambiguous phrase produces a draft that is well-formed, well-worded and wrong
/// some of the time — a failure no reply-text assertion and no judge reliably
/// notices, because nothing about it looks like a failure.
/// </summary>
public enum DateAmbiguity
{
    /// <summary>Resolved to exactly one span.</summary>
    None,

    /// <summary>
    /// "next Friday", said on a Friday. Either the Friday seven days out or the
    /// Friday of the week after; both readings have defenders.
    /// </summary>
    NextWeekdayOnTheSameWeekday,

    /// <summary>"Thursday the 13th", where the 13th is not a Thursday.</summary>
    StatedWeekdayDoesNotMatch,

    /// <summary>"Monday and Thursday" — two days with a working day between them.</summary>
    NonContiguousDates,

    /// <summary>The span runs backwards.</summary>
    EndBeforeStart,

    /// <summary>Nothing in the sentence named a date at all.</summary>
    NoDateGiven,
}

/// <summary>
/// The outcome of resolving one <see cref="DateExpression"/> against a pinned
/// local date.
/// </summary>
/// <param name="Start">Inclusive first day, when resolved.</param>
/// <param name="End">Inclusive last day, when resolved.</param>
/// <param name="Ambiguity">Why it did not resolve, or <see cref="DateAmbiguity.None"/>.</param>
/// <param name="Readings">
/// The candidate dates, when there is more than one. Carried so the agent can put
/// them in the question it asks — "did you mean the 21st or the 28th?" is a useful
/// question and "which Friday?" is not.
/// </param>
public sealed record DateResolution(
    DateOnly? Start,
    DateOnly? End,
    DateAmbiguity Ambiguity,
    IReadOnlyList<DateOnly> Readings)
{
    public bool IsResolved => Ambiguity == DateAmbiguity.None && Start is not null && End is not null;

    public static DateResolution Resolved(DateOnly start, DateOnly end) =>
        new(start, end, DateAmbiguity.None, []);

    public static DateResolution Ambiguous(DateAmbiguity reason, params DateOnly[] readings) =>
        new(null, null, reason, readings);
}
