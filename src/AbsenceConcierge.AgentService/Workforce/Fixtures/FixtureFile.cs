using System.Globalization;

namespace AbsenceConcierge.AgentService.Workforce.Fixtures;

/// <summary>
/// The on-disk shape of <c>evals/fixtures/*.yaml</c>.
///
/// These types are deliberately dumb and mutable: they exist to be deserialised and
/// then mapped into the immutable domain records in <c>WorkforceModels.cs</c>. Dates
/// are strings here and <see cref="DateOnly"/> after mapping, so a malformed date in
/// a fixture fails at load with the file and value named, rather than silently
/// becoming a default somewhere downstream.
///
/// The same file feeds the mock tools and the eval scenarios. One source of truth
/// for the world — two copies would drift, and the drift would show up as an eval
/// that passes against a world the service no longer has.
/// </summary>
public sealed class FixtureFile
{
    public int Version { get; set; }
    public string Name { get; set; } = string.Empty;
    public string? Description { get; set; }

    public FixtureActor Actor { get; set; } = new();
    public List<FixtureEmployee> Employees { get; set; } = [];
    public List<FixtureLeaveType> LeaveTypes { get; set; } = [];
    public List<FixtureLeave> ExistingLeaves { get; set; } = [];
    public List<FixtureHoliday> CompanyHolidays { get; set; } = [];
    public FixtureWorkingPattern WorkingPattern { get; set; } = new();
}

public sealed class FixtureActor
{
    public string EmployeeId { get; set; } = string.Empty;
    public string DisplayName { get; set; } = string.Empty;
    public string Team { get; set; } = string.Empty;
    public string? ManagerEmployeeId { get; set; }
    public List<string> Permissions { get; set; } = [];
}

public sealed class FixtureEmployee
{
    public string EmployeeId { get; set; } = string.Empty;
    public string DisplayName { get; set; } = string.Empty;
    public string Team { get; set; } = string.Empty;
}

public sealed class FixtureLeaveType
{
    public string Id { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public bool RequiresApproval { get; set; }
    public bool CountsAgainstBalance { get; set; }
    public int MaxConsecutiveDays { get; set; }
    public bool AllowsHalfDays { get; set; }
    public int? RequiresAttachmentAfterDays { get; set; }
}

public sealed class FixtureLeave
{
    public string Id { get; set; } = string.Empty;
    public string EmployeeId { get; set; } = string.Empty;
    public string LeaveTypeId { get; set; } = string.Empty;
    public string StartDate { get; set; } = string.Empty;
    public string EndDate { get; set; } = string.Empty;
    public string Status { get; set; } = string.Empty;
}

public sealed class FixtureHoliday
{
    public string Date { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
}

public sealed class FixtureWorkingPattern
{
    public List<string> WorkingDays { get; set; } = [];
    public int HoursPerDay { get; set; } = 8;
}

/// <summary>
/// The mapped, immutable world the tools serve. Built once per fixture load.
/// </summary>
public sealed record WorkforceWorld(
    string Name,
    WorkforceUser Actor,
    IReadOnlyList<Employee> Employees,
    IReadOnlyList<LeaveType> LeaveTypes,
    IReadOnlyList<Leave> ExistingLeaves,
    IReadOnlyList<CompanyHoliday> CompanyHolidays,
    IReadOnlySet<DayOfWeek> WorkingDays)
{
    public static WorkforceWorld FromFile(FixtureFile file, string source)
    {
        DateOnly Date(string value, string field) =>
            DateOnly.TryParse(value, CultureInfo.InvariantCulture, out var parsed)
                ? parsed
                : throw new InvalidOperationException(
                    $"Fixture '{source}' has an unparseable date in {field}: '{value}'.");

        var workingDays = file.WorkingPattern.WorkingDays
            .Select(day => Enum.TryParse<DayOfWeek>(day, ignoreCase: true, out var parsed)
                ? parsed
                : throw new InvalidOperationException(
                    $"Fixture '{source}' has an unrecognised working day: '{day}'."))
            .ToHashSet();

        if (workingDays.Count == 0)
        {
            // A world with no working days makes every request land on a non-working
            // day, which would look like a subtle agent bug rather than a broken
            // fixture. Fail at load, where the cause is obvious.
            throw new InvalidOperationException($"Fixture '{source}' declares no working days.");
        }

        return new WorkforceWorld(
            file.Name,
            new WorkforceUser(
                file.Actor.EmployeeId,
                file.Actor.DisplayName,
                file.Actor.Team,
                file.Actor.Permissions.AsReadOnly()),
            file.Employees
                .Select(e => new Employee(e.EmployeeId, e.DisplayName, e.Team))
                .ToList(),
            file.LeaveTypes
                .Select(t => new LeaveType(
                    t.Id,
                    t.Name,
                    t.RequiresApproval,
                    t.CountsAgainstBalance,
                    t.MaxConsecutiveDays,
                    t.AllowsHalfDays,
                    t.RequiresAttachmentAfterDays))
                .ToList(),
            file.ExistingLeaves
                .Select(l => new Leave(
                    l.Id,
                    l.EmployeeId,
                    l.LeaveTypeId,
                    Date(l.StartDate, $"existing_leaves[{l.Id}].start_date"),
                    Date(l.EndDate, $"existing_leaves[{l.Id}].end_date"),
                    l.Status))
                .ToList(),
            file.CompanyHolidays
                .Select(h => new CompanyHoliday(Date(h.Date, $"company_holidays[{h.Name}].date"), h.Name))
                .ToList(),
            workingDays);
    }
}
