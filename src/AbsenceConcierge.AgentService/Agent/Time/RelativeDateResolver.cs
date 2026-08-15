namespace AbsenceConcierge.AgentService.Agent.Time;

/// <summary>
/// Turns a <see cref="DateExpression"/> into calendar dates, against a pinned
/// local date. Pure: no clock, no timezone, no configuration, no IO.
///
/// <para>
/// <b>Everything here is <see cref="DateOnly"/> arithmetic, and that is the point.</b>
/// The classic defect this design removes is resolving "next Friday" by adding
/// 7 × 24 hours to an instant. That is arithmetically fine and lands an hour early
/// in local terms across a daylight-saving transition — which either moves the day
/// boundary or moves the date outright, producing a request for the wrong day from
/// code that looks correct. A calendar date plus seven days is seven days in every
/// timezone and on both sides of every transition. The only place an instant is
/// converted to a date is <see cref="AgentClock.Today"/>, once, at the edge.
/// </para>
/// <para>
/// The corresponding scenario is <c>amb-004</c>; the corresponding unit tests pin
/// the arithmetic directly, because a scenario that only exercises a date parser is
/// an eval-budget line item forever (SPEC §9).
/// </para>
/// </summary>
public static class RelativeDateResolver
{
    public static DateResolution Resolve(DateExpression expression, DateOnly today) => expression switch
    {
        TodayExpression => DateResolution.Resolved(today, today),

        TomorrowExpression => Single(today.AddDays(1)),

        ComingWeekdayExpression weekday => Single(OnOrAfter(today, weekday.Day)),

        // Said on the same weekday, "next Friday" has two readings and the agent
        // does not get to pick. Said on any other day it means the Friday coming.
        NextWeekdayExpression weekday when weekday.Day == today.DayOfWeek =>
            DateResolution.Ambiguous(
                DateAmbiguity.NextWeekdayOnTheSameWeekday,
                today.AddDays(7),
                today.AddDays(14)),

        NextWeekdayExpression weekday => Single(After(today, weekday.Day)),

        WeekdayNextWeekExpression weekday => Single(InTheWeekAfter(today, weekday.Day)),

        CalendarDayExpression day => ResolveCalendarDay(day, today),

        DateSpanExpression span => ResolveSpan(span, today),

        DateListExpression list => ResolveList(list, today),

        _ => DateResolution.Ambiguous(DateAmbiguity.NoDateGiven),
    };

    private static DateResolution Single(DateOnly date) => DateResolution.Resolved(date, date);

    /// <summary>The next occurrence of <paramref name="day"/> on or after <paramref name="from"/>.</summary>
    private static DateOnly OnOrAfter(DateOnly from, DayOfWeek day) =>
        from.AddDays(((int)day - (int)from.DayOfWeek + 7) % 7);

    /// <summary>The next occurrence of <paramref name="day"/> strictly after <paramref name="from"/>.</summary>
    private static DateOnly After(DateOnly from, DayOfWeek day)
    {
        var offset = ((int)day - (int)from.DayOfWeek + 7) % 7;
        return from.AddDays(offset == 0 ? 7 : offset);
    }

    /// <summary>
    /// The named day of the calendar week after this one, where a week starts on
    /// Monday. Monday-first rather than Sunday-first because the working pattern in
    /// every fixture is Monday to Friday, and "Friday next week" said on a Sunday
    /// should not mean yesterday's week.
    /// </summary>
    private static DateOnly InTheWeekAfter(DateOnly from, DayOfWeek day)
    {
        var daysSinceMonday = ((int)from.DayOfWeek - (int)DayOfWeek.Monday + 7) % 7;
        var nextMonday = from.AddDays(7 - daysSinceMonday);
        return nextMonday.AddDays(((int)day - (int)DayOfWeek.Monday + 7) % 7);
    }

    private static DateResolution ResolveCalendarDay(CalendarDayExpression expression, DateOnly today)
    {
        var candidate = NextOccurrenceOfDayOfMonth(expression.Day, expression.Month, today);

        if (candidate is null)
        {
            // The 31st of a 30-day month, or the 30th of February. Not a date, and
            // not something to round into one.
            return DateResolution.Ambiguous(DateAmbiguity.NoDateGiven);
        }

        if (expression.StatedWeekday is { } stated && candidate.Value.DayOfWeek != stated)
        {
            // The user named both a weekday and a date and they disagree. One of
            // them is a slip and the agent cannot know which.
            return DateResolution.Ambiguous(DateAmbiguity.StatedWeekdayDoesNotMatch, candidate.Value);
        }

        return Single(candidate.Value);
    }

    /// <summary>
    /// The first date matching a day-of-month (and optionally a month) that is not
    /// in the past. Rolls the year forward rather than the month when a month was
    /// named: "the 9th of October", said in November, means next October.
    /// </summary>
    private static DateOnly? NextOccurrenceOfDayOfMonth(int dayOfMonth, int? month, DateOnly today)
    {
        if (dayOfMonth is < 1 or > 31)
        {
            return null;
        }

        // Two years of candidates is enough for every "the 9th of October" a user
        // can mean, and terminates whatever the input.
        for (var offset = 0; offset <= 24; offset++)
        {
            var anchor = new DateOnly(today.Year, today.Month, 1).AddMonths(offset);

            if (month is { } named && anchor.Month != named)
            {
                continue;
            }

            if (dayOfMonth > DateTime.DaysInMonth(anchor.Year, anchor.Month))
            {
                continue;
            }

            var candidate = new DateOnly(anchor.Year, anchor.Month, dayOfMonth);

            if (candidate >= today)
            {
                return candidate;
            }
        }

        return null;
    }

    private static DateResolution ResolveSpan(DateSpanExpression span, DateOnly today)
    {
        var from = Resolve(span.From, today);

        if (!from.IsResolved)
        {
            return from;
        }

        // The end is resolved from the start, not from today, so "the 20th to the
        // 24th" cannot straddle a month or year boundary backwards, and "Monday to
        // Friday" takes the Friday of the week the Monday landed in.
        var to = Resolve(span.To, from.Start!.Value);

        if (!to.IsResolved)
        {
            return to;
        }

        return to.End!.Value < from.Start.Value
            ? DateResolution.Ambiguous(DateAmbiguity.EndBeforeStart, from.Start.Value, to.End.Value)
            : DateResolution.Resolved(from.Start.Value, to.End.Value);
    }

    private static DateResolution ResolveList(DateListExpression list, DateOnly today)
    {
        if (list.Parts.Count == 0)
        {
            return DateResolution.Ambiguous(DateAmbiguity.NoDateGiven);
        }

        var dates = new List<DateOnly>(list.Parts.Count);
        var anchor = today;

        foreach (var part in list.Parts)
        {
            var resolved = Resolve(part, anchor);

            if (!resolved.IsResolved)
            {
                return resolved;
            }

            dates.Add(resolved.Start!.Value);

            // Each item is resolved from the one before it, so "Thursday and Friday"
            // does not resolve both to the same week only by luck, and "the 31st and
            // the 1st" walks into the next month rather than backwards.
            anchor = resolved.Start.Value;
        }

        dates.Sort();

        for (var i = 1; i < dates.Count; i++)
        {
            if (dates[i].DayNumber - dates[i - 1].DayNumber != 1)
            {
                // Deliberately strict: gaps are not closed silently, even a weekend
                // one. A single request covers a single span, and "Friday and Monday"
                // is two requests or a misunderstanding — either way a question.
                return DateResolution.Ambiguous(DateAmbiguity.NonContiguousDates, [.. dates]);
            }
        }

        return DateResolution.Resolved(dates[0], dates[^1]);
    }
}
