using AbsenceConcierge.AgentService.Agent.Time;

namespace AbsenceConcierge.AgentService.Tests;

/// <summary>
/// The date arithmetic, pinned.
///
/// <para>
/// SPEC §9 requires this twice over: the ambiguity scenarios assert that the agent
/// <em>asks</em> rather than guesses, and the arithmetic itself — month rollover,
/// year rollover, the weekday a phrase is said on — is pinned here, because a
/// scenario that only exercises a date parser is an eval-budget line item forever.
/// Both exist and they test different things.
/// </para>
/// <para>
/// Not one of these tests mentions a sentence, and that is the design working: the
/// parser produces expressions, the resolver resolves them, and neither needs the
/// other to be tested.
/// </para>
/// </summary>
public sealed class RelativeDateResolverTests
{
    private static readonly DateOnly Tuesday = new(2026, 8, 11);

    [Fact]
    public void Today_is_the_pinned_date_and_nothing_else()
    {
        var resolved = RelativeDateResolver.Resolve(new TodayExpression(), Tuesday);

        Assert.True(resolved.IsResolved);
        Assert.Equal(Tuesday, resolved.Start);
        Assert.Equal(Tuesday, resolved.End);
    }

    [Fact]
    public void Tomorrow_is_the_next_calendar_day_even_when_it_is_a_weekend()
    {
        // Whether it is a working day is the calendar's business, not the
        // resolver's. Conflating them would make "tomorrow" mean different dates in
        // companies with different working patterns.
        var friday = new DateOnly(2026, 8, 14);
        var resolved = RelativeDateResolver.Resolve(new TomorrowExpression(), friday);

        Assert.Equal(new DateOnly(2026, 8, 15), resolved.Start);
    }

    [Theory]
    [InlineData(DayOfWeek.Friday, "2026-08-14")]
    [InlineData(DayOfWeek.Monday, "2026-08-17")]
    [InlineData(DayOfWeek.Thursday, "2026-08-13")]
    public void A_bare_weekday_is_the_next_one_on_or_after_today(DayOfWeek day, string expected)
    {
        var resolved = RelativeDateResolver.Resolve(new ComingWeekdayExpression(day), Tuesday);

        Assert.Equal(DateOnly.Parse(expected, System.Globalization.CultureInfo.InvariantCulture), resolved.Start);
    }

    [Fact]
    public void A_bare_weekday_that_is_today_means_today()
    {
        // "Monday to Friday", said on a Monday, is this week. Resolving it strictly
        // after today would push the whole request seven days out and produce a
        // well-formed request for the wrong week.
        var monday = new DateOnly(2026, 9, 7);
        var resolved = RelativeDateResolver.Resolve(new ComingWeekdayExpression(DayOfWeek.Monday), monday);

        Assert.Equal(monday, resolved.Start);
    }

    [Fact]
    public void Next_friday_said_on_a_monday_is_the_friday_of_that_week_across_a_month_boundary()
    {
        // The classic defects are keeping the month while advancing the day
        // (2026-08-04) and an off-by-one that lands on the Saturday (2026-09-05).
        var lastMondayOfAugust = new DateOnly(2026, 8, 31);
        var resolved = RelativeDateResolver.Resolve(new NextWeekdayExpression(DayOfWeek.Friday), lastMondayOfAugust);

        Assert.Equal(new DateOnly(2026, 9, 4), resolved.Start);
        Assert.Equal(DayOfWeek.Friday, resolved.Start!.Value.DayOfWeek);
    }

    [Fact]
    public void Next_friday_said_midweek_crosses_a_year_boundary_without_help()
    {
        var wednesday = new DateOnly(2026, 12, 30);
        var resolved = RelativeDateResolver.Resolve(new NextWeekdayExpression(DayOfWeek.Friday), wednesday);

        Assert.Equal(new DateOnly(2027, 1, 1), resolved.Start);
    }

    [Fact]
    public void Next_friday_said_on_a_friday_has_two_readings_and_resolves_to_neither()
    {
        // The sharpest sentence in the suite. A competent English speaker will
        // defend either reading, so an agent that picks one is flipping a coin and
        // reporting the outcome as fact.
        var friday = new DateOnly(2026, 8, 14);
        var resolved = RelativeDateResolver.Resolve(new NextWeekdayExpression(DayOfWeek.Friday), friday);

        Assert.False(resolved.IsResolved);
        Assert.Equal(DateAmbiguity.NextWeekdayOnTheSameWeekday, resolved.Ambiguity);
        Assert.Equal([new DateOnly(2026, 8, 21), new DateOnly(2026, 8, 28)], resolved.Readings);
    }

    [Fact]
    public void Friday_next_week_is_unambiguous_on_every_day_of_the_week()
    {
        // The phrase amb-004 uses, and the reason it can assert a date where amb-001
        // asserts a question: "next week" names a specific calendar week whatever
        // day you say it on.
        var expected = new DateOnly(2026, 10, 30);

        for (var offset = 0; offset < 7; offset++)
        {
            var spokenOn = new DateOnly(2026, 10, 19).AddDays(offset);
            var resolved = RelativeDateResolver.Resolve(
                new WeekdayNextWeekExpression(DayOfWeek.Friday),
                spokenOn);

            Assert.True(resolved.IsResolved, $"unresolved when said on {spokenOn:yyyy-MM-dd}");
            Assert.Equal(expected, resolved.Start);
        }
    }

    [Fact]
    public void A_day_of_month_rolls_forward_rather_than_backward()
    {
        var resolved = RelativeDateResolver.Resolve(new CalendarDayExpression(9, 10, null), Tuesday);

        Assert.Equal(new DateOnly(2026, 10, 9), resolved.Start);
    }

    [Fact]
    public void A_month_already_past_this_year_means_next_year()
    {
        var november = new DateOnly(2026, 11, 1);
        var resolved = RelativeDateResolver.Resolve(new CalendarDayExpression(9, 10, null), november);

        Assert.Equal(new DateOnly(2027, 10, 9), resolved.Start);
    }

    [Fact]
    public void A_day_of_month_with_no_month_takes_the_next_occurrence()
    {
        var resolved = RelativeDateResolver.Resolve(new CalendarDayExpression(24, null, null), Tuesday);

        Assert.Equal(new DateOnly(2026, 8, 24), resolved.Start);
    }

    [Fact]
    public void A_stated_weekday_that_matches_is_accepted()
    {
        var resolved = RelativeDateResolver.Resolve(
            new CalendarDayExpression(13, null, DayOfWeek.Thursday),
            Tuesday);

        Assert.Equal(new DateOnly(2026, 8, 13), resolved.Start);
    }

    [Fact]
    public void A_stated_weekday_that_does_not_match_is_a_question()
    {
        // "Wednesday the 13th", where the 13th is a Thursday. One of the two is a
        // slip and the agent cannot know which — so it asks rather than silently
        // preferring the number over the word.
        var resolved = RelativeDateResolver.Resolve(
            new CalendarDayExpression(13, null, DayOfWeek.Wednesday),
            Tuesday);

        Assert.False(resolved.IsResolved);
        Assert.Equal(DateAmbiguity.StatedWeekdayDoesNotMatch, resolved.Ambiguity);
    }

    [Fact]
    public void A_day_that_never_exists_in_the_named_month_resolves_to_nothing()
    {
        var resolved = RelativeDateResolver.Resolve(new CalendarDayExpression(31, 2, null), Tuesday);

        Assert.False(resolved.IsResolved);
        Assert.Equal(DateAmbiguity.NoDateGiven, resolved.Ambiguity);
    }

    [Fact]
    public void A_span_of_weekdays_stays_inside_one_week()
    {
        var monday = new DateOnly(2026, 9, 7);
        var resolved = RelativeDateResolver.Resolve(
            new DateSpanExpression(
                new ComingWeekdayExpression(DayOfWeek.Monday),
                new ComingWeekdayExpression(DayOfWeek.Friday)),
            monday);

        Assert.Equal(new DateOnly(2026, 9, 7), resolved.Start);
        Assert.Equal(new DateOnly(2026, 9, 11), resolved.End);
    }

    [Fact]
    public void A_span_of_days_of_the_month_resolves_both_ends()
    {
        var resolved = RelativeDateResolver.Resolve(
            new DateSpanExpression(
                new CalendarDayExpression(9, 10, null),
                new CalendarDayExpression(13, 10, null)),
            new DateOnly(2026, 10, 8));

        Assert.Equal(new DateOnly(2026, 10, 9), resolved.Start);
        Assert.Equal(new DateOnly(2026, 10, 13), resolved.End);
    }

    [Fact]
    public void A_month_crossing_span_resolves_into_the_month_before_the_named_one()
    {
        // "The 30th to the 2nd of May", said in April. The parser leaves the 30th
        // monthless rather than back-propagating May across a descending pair, so
        // next-occurrence resolution puts it on 30 April and the span is the three
        // days that were asked for. With May stamped on both ends this resolved to
        // 30 May 2026 – 2 May 2027: eleven months, and no question asked.
        var resolved = RelativeDateResolver.Resolve(
            new DateSpanExpression(
                new CalendarDayExpression(30, null, null),
                new CalendarDayExpression(2, 5, null)),
            new DateOnly(2026, 4, 15));

        Assert.Equal(new DateOnly(2026, 4, 30), resolved.Start);
        Assert.Equal(new DateOnly(2026, 5, 2), resolved.End);
    }

    [Fact]
    public void A_list_of_adjacent_days_becomes_one_span()
    {
        var resolved = RelativeDateResolver.Resolve(
            new DateListExpression([new TodayExpression(), new TomorrowExpression()]),
            Tuesday);

        Assert.Equal(Tuesday, resolved.Start);
        Assert.Equal(new DateOnly(2026, 8, 12), resolved.End);
    }

    [Fact]
    public void A_list_with_a_gap_is_a_question_rather_than_a_silently_widened_span()
    {
        // "Monday and Wednesday" is two requests or a misunderstanding. Filling in
        // the Tuesday would book a day the user never asked for.
        var resolved = RelativeDateResolver.Resolve(
            new DateListExpression(
            [
                new ComingWeekdayExpression(DayOfWeek.Monday),
                new ComingWeekdayExpression(DayOfWeek.Wednesday),
            ]),
            Tuesday);

        Assert.False(resolved.IsResolved);
        Assert.Equal(DateAmbiguity.NonContiguousDates, resolved.Ambiguity);
    }

    [Fact]
    public void A_week_forward_across_a_daylight_saving_change_lands_on_the_right_local_day()
    {
        // Europe/Madrid moves from +02:00 to +01:00 on 2026-10-25, and this is the
        // scenario amb-004 exists for. The second half of the test is the point: it
        // performs the arithmetic this design refuses to use, and shows it landing a
        // day short. Without it, the first assertion passes under an implementation
        // that has the bug and merely happens not to hit it.
        var madrid = TimeZoneInfo.FindSystemTimeZoneById("Europe/Madrid");
        var spokenOnFriday = new DateOnly(2026, 10, 23);

        var resolved = RelativeDateResolver.Resolve(
            new WeekdayNextWeekExpression(DayOfWeek.Friday),
            spokenOnFriday);

        Assert.Equal(new DateOnly(2026, 10, 30), resolved.Start);

        // Local midnight on the day it was said, plus seven times twenty-four hours,
        // read back in the same zone: 2026-10-29, not the 30th.
        var localMidnight = new DateTimeOffset(2026, 10, 23, 0, 0, 0, TimeSpan.FromHours(2));
        var naive = TimeZoneInfo.ConvertTime(localMidnight.AddHours(7 * 24), madrid);

        Assert.Equal(new DateOnly(2026, 10, 29), DateOnly.FromDateTime(naive.DateTime));
    }

    [Fact]
    public void Next_friday_said_on_the_day_amb_004_uses_is_still_ambiguous()
    {
        // The rule holds everywhere or it is not a rule. 2026-10-23 is a Friday, so
        // "next Friday" said on it has the same two readings as amb-001's sentence —
        // which is why amb-004 says "Friday next week" instead. This test is the one
        // that caught the corpus defect: it was written the other way round first,
        // and it failed.
        var resolved = RelativeDateResolver.Resolve(
            new NextWeekdayExpression(DayOfWeek.Friday),
            new DateOnly(2026, 10, 23));

        Assert.False(resolved.IsResolved);
        Assert.Equal(DateAmbiguity.NextWeekdayOnTheSameWeekday, resolved.Ambiguity);
    }
}
