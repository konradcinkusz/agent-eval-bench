using AbsenceConcierge.AgentService.Agent.Time;

namespace AbsenceConcierge.AgentService.Tests;

/// <summary>
/// The working-day count and the excluded days, against the real fixture's calendar.
///
/// B-11 requires the agent to say <em>which</em> days it did not count, so the count
/// and the exclusions are tested together: a number without the reasons behind it
/// cannot be checked by the human approving it.
/// </summary>
public sealed class WorkingCalendarTests
{
    private static WorkingCalendar Calendar() => WorkingCalendar.FromWorld(TestWorld.Load());

    [Fact]
    public void A_plain_week_counts_five_days_and_excludes_the_weekend()
    {
        // Monday 2026-09-07 to Friday 2026-09-11: no holidays in that week.
        var count = Calendar().Count(new DateOnly(2026, 9, 7), new DateOnly(2026, 9, 11));

        Assert.Equal(5, count.WorkingDays);
        Assert.Empty(count.Excluded);
    }

    [Fact]
    public void A_span_containing_a_weekend_and_a_holiday_counts_neither()
    {
        // 2026-10-09 (Fri) to 2026-10-13 (Tue). The 10th and 11th are the weekend
        // and the 12th is National Day, so five calendar days cost two.
        var count = Calendar().Count(new DateOnly(2026, 10, 9), new DateOnly(2026, 10, 13));

        Assert.Equal(2, count.WorkingDays);
        Assert.Equal(3, count.Excluded.Count);

        Assert.Equal(
            [WorkingCalendar.WeekendReason, WorkingCalendar.WeekendReason, WorkingCalendar.HolidayReason],
            count.Excluded.Select(day => day.Reason));

        var holiday = count.Excluded[^1];
        Assert.Equal(new DateOnly(2026, 10, 12), holiday.Date);
        Assert.Equal("National Day", holiday.Label);
    }

    [Fact]
    public void A_holiday_that_falls_on_a_weekend_is_reported_as_a_weekend_day()
    {
        // 2026-08-15 is Assumption and also a Saturday. Reporting it as an excluded
        // holiday would imply it cost the employee something; it did not, and the
        // request must not be silently extended by it either.
        var calendar = Calendar();
        var saturday = new DateOnly(2026, 8, 15);

        Assert.False(calendar.IsWorkingDay(saturday));
        Assert.Equal("Assumption", calendar.HolidayName(saturday));

        var count = calendar.Count(saturday, saturday);

        Assert.Equal(0, count.WorkingDays);
        Assert.Equal(WorkingCalendar.WeekendReason, Assert.Single(count.Excluded).Reason);
    }

    [Fact]
    public void A_single_working_day_counts_one()
    {
        var count = Calendar().Count(new DateOnly(2026, 8, 11), new DateOnly(2026, 8, 11));

        Assert.Equal(1, count.WorkingDays);
        Assert.Empty(count.Excluded);
    }
}

/// <summary>
/// The one place an instant becomes a date, and the reason it is the only one.
/// </summary>
public sealed class AgentClockTests
{
    private static readonly TimeZoneInfo Madrid = TimeZoneInfo.FindSystemTimeZoneById("Europe/Madrid");

    [Fact]
    public void Today_is_the_actors_local_date_not_the_utc_one()
    {
        // 00:30 in Madrid is 22:30 the previous day in UTC. An agent that read the
        // date off the UTC instant would resolve "today" to yesterday for every
        // user in the zone, for half an hour a night, and every test on a UTC CI
        // runner would still pass.
        var justAfterMidnight = new DateTimeOffset(2026, 8, 11, 0, 30, 0, TimeSpan.FromHours(2));
        var clock = new AgentClock(new FixedTimeProvider(justAfterMidnight), Madrid);

        Assert.Equal(new DateOnly(2026, 8, 11), clock.Today);
        Assert.Equal(new DateOnly(2026, 8, 10), DateOnly.FromDateTime(justAfterMidnight.UtcDateTime));
    }

    [Fact]
    public void Today_is_correct_on_both_sides_of_a_daylight_saving_change()
    {
        // Europe/Madrid leaves summer time on 2026-10-25.
        var before = new AgentClock(
            new FixedTimeProvider(new DateTimeOffset(2026, 10, 23, 9, 0, 0, TimeSpan.FromHours(2))),
            Madrid);

        var after = new AgentClock(
            new FixedTimeProvider(new DateTimeOffset(2026, 10, 30, 9, 0, 0, TimeSpan.FromHours(1))),
            Madrid);

        Assert.Equal(new DateOnly(2026, 10, 23), before.Today);
        Assert.Equal(new DateOnly(2026, 10, 30), after.Today);
    }
}
