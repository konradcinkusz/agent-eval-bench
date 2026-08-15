using AbsenceConcierge.AgentService.Workforce;
using AbsenceConcierge.AgentService.Workforce.Fixtures;
using Microsoft.Extensions.Logging.Abstractions;

namespace AbsenceConcierge.AgentService.Tests;

/// <summary>
/// The fixture is the world every scenario runs against, so a silent mis-parse here
/// would make thirty-two scenarios wrong in a way none of them could report.
/// </summary>
public sealed class WorkforceFixtureTests
{
    [Fact]
    public void The_actor_and_their_permissions_load()
    {
        var world = TestWorld.Load();

        Assert.Equal(TestWorld.ActorEmployeeId, world.Actor.EmployeeId);
        Assert.Equal(
            new[] { Permissions.DirectoryRead, Permissions.TimeOffRead, Permissions.TimeOffRequest },
            world.Actor.Permissions);
    }

    [Fact]
    public void Dates_parse_rather_than_defaulting()
    {
        var world = TestWorld.Load();

        var booking = Assert.Single(world.ExistingLeaves, l => l.Id == "lv-3001");
        Assert.Equal(new DateOnly(2026, 8, 20), booking.StartDate);
        Assert.Equal(new DateOnly(2026, 8, 22), booking.EndDate);

        Assert.Contains(world.CompanyHolidays, h => h.Date == new DateOnly(2026, 8, 15));
    }

    [Fact]
    public void The_certificate_threshold_survives_the_round_trip()
    {
        // A nullable int that quietly becomes null would make B-14 untestable while
        // every scenario still passed.
        var world = TestWorld.Load();

        var sick = Assert.Single(world.LeaveTypes, t => t.Id == TestWorld.SickTypeId);
        Assert.Equal(3, sick.RequiresAttachmentAfterDays);

        var vacation = Assert.Single(world.LeaveTypes, t => t.Id == TestWorld.VacationTypeId);
        Assert.Null(vacation.RequiresAttachmentAfterDays);
    }

    [Fact]
    public void Working_days_exclude_the_weekend()
    {
        var world = TestWorld.Load();

        Assert.Contains(DayOfWeek.Monday, world.WorkingDays);
        Assert.DoesNotContain(DayOfWeek.Saturday, world.WorkingDays);
        Assert.DoesNotContain(DayOfWeek.Sunday, world.WorkingDays);
    }

    [Fact]
    public void Two_colleagues_share_a_name()
    {
        // Not incidental data. amb-005 exists because of these two rows, and a
        // fixture edit that de-duplicated them would silently defuse that scenario.
        var world = TestWorld.Load();

        var sams = world.Employees.Where(e => e.DisplayName == "Sam Rivera").ToList();
        Assert.Equal(2, sams.Count);
        Assert.NotEqual(sams[0].Team, sams[1].Team);
    }

    [Fact]
    public void A_missing_fixture_fails_loudly_and_names_what_is_available()
    {
        var loader = new FixtureLoader(NullLogger<FixtureLoader>.Instance, TestWorld.FixtureDirectory);

        var ex = Assert.Throws<FileNotFoundException>(() => loader.Load("no-such-world"));

        // "Fail loudly rather than reporting success over an empty set." An empty
        // world would make every scenario fail for the same uninformative reason.
        Assert.Contains("meridian-labs", ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void A_missing_fixture_directory_fails_loudly()
    {
        var loader = new FixtureLoader(
            NullLogger<FixtureLoader>.Instance,
            Path.Combine(AppContext.BaseDirectory, "fixtures-that-do-not-exist"));

        Assert.Throws<DirectoryNotFoundException>(() => loader.Load("meridian-labs"));
    }
}

/// <summary>
/// The read/write classification is the definition of "write-classified" that C-1
/// gates on. It is data, and these tests are what stop it becoming a convention.
/// </summary>
public sealed class WorkforceToolCatalogTests
{
    [Fact]
    public void Exactly_one_tool_is_a_write()
    {
        var writes = WorkforceToolCatalog.Names.Where(WorkforceToolCatalog.IsWrite).ToList();

        Assert.Equal([WorkforceToolCatalog.RequestTimeOff], writes);
    }

    [Fact]
    public void An_unclassified_tool_throws_rather_than_defaulting_to_read()
    {
        // The load-bearing test in this file. If an unknown tool defaulted to Read,
        // then "we forgot to classify the new write" and "this is a read" would be
        // the same answer, and C-1 would quietly stop covering the new tool.
        Assert.Throws<ArgumentOutOfRangeException>(() => WorkforceToolCatalog.KindOf("delete_everything"));
    }

    [Fact]
    public void Every_tool_declares_its_permission()
    {
        foreach (var tool in WorkforceToolCatalog.Names)
        {
            // get_current_user needs none; the rest must name one.
            var permission = WorkforceToolCatalog.RequiredPermission(tool);
            if (tool != WorkforceToolCatalog.GetCurrentUser)
            {
                Assert.False(string.IsNullOrWhiteSpace(permission), $"{tool} declares no permission");
            }
        }
    }
}

/// <summary>Denied paths, enforced at the boundary rather than in the prompt.</summary>
public sealed class WorkforcePermissionTests
{
    [Fact]
    public async Task Without_the_request_permission_a_write_is_denied()
    {
        var world = TestWorld.WithPermissions(Permissions.DirectoryRead, Permissions.TimeOffRead);
        var (tools, tokens, _) = TestWorld.Build(world);
        var start = new DateOnly(2026, 8, 26);
        var token = TestWorld.ApprovedToken(tokens, TestWorld.VacationTypeId, start, start);

        var result = await tools.RequestTimeOffAsync(
            new TimeOffRequest(TestWorld.VacationTypeId, start, start, token));

        Assert.Equal(ToolOutcome.PermissionDenied, result.Outcome);
    }

    [Fact]
    public async Task A_denial_message_names_the_capability_and_never_the_permission_string()
    {
        // C-3 and O-7. "You lack timeoff:request" satisfies a naive reading of the
        // refusal requirement while leaking exactly what C-3 forbids.
        var world = TestWorld.WithPermissions(Permissions.DirectoryRead, Permissions.TimeOffRead);
        var (tools, tokens, _) = TestWorld.Build(world);
        var start = new DateOnly(2026, 8, 26);
        var token = TestWorld.ApprovedToken(tokens, TestWorld.VacationTypeId, start, start);

        var result = await tools.RequestTimeOffAsync(
            new TimeOffRequest(TestWorld.VacationTypeId, start, start, token));

        Assert.NotNull(result.Message);
        Assert.DoesNotContain("timeoff:", result.Message!, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("request time off", result.Message!, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Leaves_are_only_ever_the_actors_own()
    {
        // The fixture carries a colleague's booking (lv-3003) as a distractor.
        var (tools, _, _) = TestWorld.Build();

        var result = await tools.ListLeavesAsync();

        Assert.Equal(ToolOutcome.Success, result.Outcome);
        Assert.NotEmpty(result.Value!);
        Assert.All(result.Value!, leave => Assert.Equal(TestWorld.ActorEmployeeId, leave.EmployeeId));
    }

    [Fact]
    public async Task Looking_up_an_ambiguous_name_returns_both_people()
    {
        var (tools, _, _) = TestWorld.Build();

        var result = await tools.FindEmployeeAsync("Sam");

        Assert.Equal(ToolOutcome.Success, result.Outcome);
        Assert.Equal(2, result.Value!.Count);
    }
}
