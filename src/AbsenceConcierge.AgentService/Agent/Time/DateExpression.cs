namespace AbsenceConcierge.AgentService.Agent.Time;

/// <summary>
/// A date the user expressed, before it is a date.
///
/// This type exists to keep two jobs apart. Reading "next Friday" out of a sentence
/// is language work and belongs to the interpreter; turning it into 2026-09-04 is
/// calendar arithmetic and belongs to <see cref="RelativeDateResolver"/>. Between
/// them sits this closed set, which is why the arithmetic can be unit-tested to
/// death without a single sentence in the test file (SPEC §9, "date resolution is
/// unit-tested as well as evaluated" — the two test different things).
///
/// The set is closed on purpose. An open string, passed down for the resolver to
/// re-parse, would put the same parsing in two places and let them disagree.
/// </summary>
public abstract record DateExpression;

/// <summary>"today", "I'm sick today".</summary>
public sealed record TodayExpression : DateExpression;

/// <summary>"tomorrow", "probably tomorrow".</summary>
public sealed record TomorrowExpression : DateExpression;

/// <summary>
/// A bare weekday: "Book me Friday off", "Thursday and Friday".
///
/// Resolves to that weekday <em>on or after</em> today. On-or-after rather than
/// strictly-after because "Monday to Friday" said on a Monday means this week — and
/// strictly-after would silently push the whole request seven days out, producing a
/// well-formed request for the wrong week.
/// </summary>
public sealed record ComingWeekdayExpression(DayOfWeek Day) : DateExpression;

/// <summary>
/// "next Friday" — deliberately a different expression from
/// <see cref="ComingWeekdayExpression"/>, because it is a different sentence with a
/// different failure mode. Said midweek it means the coming Friday; said on a
/// Friday it has two readings a competent English speaker will defend, and the
/// resolver refuses to pick one.
/// </summary>
public sealed record NextWeekdayExpression(DayOfWeek Day) : DateExpression;

/// <summary>
/// "Friday next week" — the named day of the calendar week after this one.
/// Unambiguous whatever day it is said on, which is what makes it usable in a
/// scenario that is testing arithmetic rather than ambiguity.
/// </summary>
public sealed record WeekdayNextWeekExpression(DayOfWeek Day) : DateExpression;

/// <summary>
/// A day of the month, with the month and the weekday optional: "the 26th",
/// "the 9th of October", "Thursday the 13th".
///
/// <paramref name="StatedWeekday"/> is kept rather than discarded so the resolver
/// can catch a user who has miscounted — "Thursday the 13th" where the 13th is a
/// Wednesday is a question, not a date.
/// </summary>
public sealed record CalendarDayExpression(int Day, int? Month, DayOfWeek? StatedWeekday) : DateExpression;

/// <summary>"the 20th to the 24th", "Monday to Friday", "19 to 21 August".</summary>
public sealed record DateSpanExpression(DateExpression From, DateExpression To) : DateExpression;

/// <summary>"today and tomorrow", "the 26th, 27th and 28th", "Thursday and Friday".</summary>
public sealed record DateListExpression(IReadOnlyList<DateExpression> Parts) : DateExpression;
