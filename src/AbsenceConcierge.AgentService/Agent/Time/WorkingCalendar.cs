using AbsenceConcierge.AgentService.Workforce;
using AbsenceConcierge.AgentService.Workforce.Fixtures;

namespace AbsenceConcierge.AgentService.Agent.Time;

/// <summary>A day inside a requested span that does not consume leave, and why.</summary>
/// <param name="Date">The excluded day.</param>
/// <param name="Reason"><c>weekend</c> or <c>holiday</c>.</param>
/// <param name="Label">The holiday's name, when it has one. Never an identifier.</param>
public sealed record ExcludedDay(DateOnly Date, string Reason, string? Label);

/// <summary>The result of counting a span against the working pattern.</summary>
public sealed record WorkingDayCount(int WorkingDays, IReadOnlyList<ExcludedDay> Excluded);

/// <summary>
/// The working pattern and holiday calendar the actor's leave is counted against.
///
/// <para>
/// This is <em>configuration</em>, not a tool result, and the distinction matters
/// for C-5. Grounding requires that every identifier argument in a write appeared
/// in an earlier tool result; the calendar contributes no identifiers, only
/// arithmetic. It comes from the same fixture the tools serve so that the count in
/// a confirmation and the count a backend would produce cannot drift apart.
/// </para>
/// <para>
/// A holiday landing on a non-working day is counted once, not twice
/// (<c>2026-08-15</c> in the base fixture is a Saturday, and <c>hap-006</c> exists
/// to check that a request is not silently extended by it).
/// </para>
/// </summary>
public sealed class WorkingCalendar
{
    public const string WeekendReason = "weekend";
    public const string HolidayReason = "holiday";

    private readonly IReadOnlySet<DayOfWeek> _workingDays;
    private readonly Dictionary<DateOnly, string> _holidays;

    public WorkingCalendar(IReadOnlySet<DayOfWeek> workingDays, IEnumerable<CompanyHoliday> holidays)
    {
        ArgumentNullException.ThrowIfNull(workingDays);
        ArgumentNullException.ThrowIfNull(holidays);

        _workingDays = workingDays;

        // A fixture may name the same date twice; the first name wins rather than
        // the loader throwing, because a duplicated holiday is not a reason to
        // refuse to answer a question about leave.
        _holidays = [];
        foreach (var holiday in holidays)
        {
            _holidays.TryAdd(holiday.Date, holiday.Name);
        }
    }

    public static WorkingCalendar FromWorld(WorkforceWorld world)
    {
        ArgumentNullException.ThrowIfNull(world);
        return new WorkingCalendar(world.WorkingDays, world.CompanyHolidays);
    }

    public bool IsWorkingDay(DateOnly date) =>
        _workingDays.Contains(date.DayOfWeek) && !_holidays.ContainsKey(date);

    public string? HolidayName(DateOnly date) =>
        _holidays.GetValueOrDefault(date);

    /// <summary>
    /// Counts the working days in an inclusive span, and reports every day it did
    /// not count. Both halves are on the confirmation event: B-11 requires the agent
    /// to say <em>which</em> days were excluded, and a bare number cannot be checked
    /// by the human approving it.
    /// </summary>
    public WorkingDayCount Count(DateOnly start, DateOnly end)
    {
        var working = 0;
        var excluded = new List<ExcludedDay>();

        for (var date = start; date <= end; date = date.AddDays(1))
        {
            if (!_workingDays.Contains(date.DayOfWeek))
            {
                // Weekend first: a holiday on a Saturday costs the employee nothing,
                // and reporting it as an excluded holiday would imply it did.
                excluded.Add(new ExcludedDay(date, WeekendReason, null));
                continue;
            }

            if (_holidays.TryGetValue(date, out var name))
            {
                excluded.Add(new ExcludedDay(date, HolidayReason, name));
                continue;
            }

            working++;
        }

        return new WorkingDayCount(working, excluded);
    }
}
