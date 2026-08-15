using AbsenceConcierge.AgentService.Workforce.Mcp;

namespace AbsenceConcierge.AgentService.Tests;

/// <summary>
/// The mapping from a server's JSON into this repository's model.
///
/// <para>
/// The rule these tests describe is "tolerant about shape, strict about content".
/// Shape is the part that varies for no reason worth defending — casing, separators,
/// whether a list arrives bare or wrapped, whether an id is a string or a number — and
/// absorbing it here is what an anti-corruption layer is for. Content is different: a
/// booking with no dates or no employee is a payload this adapter cannot act on
/// safely, and the cost of guessing is a leave request for the wrong days.
/// </para>
/// </summary>
public sealed class McpPayloadTests
{
    [Fact]
    public void A_key_is_found_regardless_of_case_and_separators()
    {
        string[] spellings =
        [
            """{"employeeId": "emp-1", "displayName": "Robin Vale"}""",
            """{"employee_id": "emp-1", "display_name": "Robin Vale"}""",
            """{"EmployeeID": "emp-1", "Display-Name": "Robin Vale"}""",
        ];

        foreach (var spelling in spellings)
        {
            var user = McpPayloads.User(spelling);

            Assert.Equal("emp-1", user.EmployeeId);
            Assert.Equal("Robin Vale", user.DisplayName);
        }
    }

    [Fact]
    public void A_list_is_read_bare_or_wrapped()
    {
        var bare = McpPayloads.LeaveTypes("""[{"id": "lt-1", "name": "Sick leave"}]""");
        var wrapped = McpPayloads.LeaveTypes("""{"data": [{"id": "lt-1", "name": "Sick leave"}]}""");
        var named = McpPayloads.LeaveTypes("""{"leave_types": [{"id": "lt-1", "name": "Sick leave"}]}""");

        Assert.Equal("lt-1", Assert.Single(bare).Id);
        Assert.Equal(bare, wrapped);
        Assert.Equal(bare, named);
    }

    [Fact]
    public void An_identifier_that_arrived_as_a_number_is_still_an_identifier()
    {
        var employees = McpPayloads.Employees("""[{"id": 4711, "name": "Robin Vale", "team": "Platform"}]""");

        Assert.Equal("4711", Assert.Single(employees).EmployeeId);
    }

    [Fact]
    public void A_timestamp_is_read_as_the_date_the_server_meant()
    {
        // The offset is honoured rather than dropped. Converting to UTC first would
        // move a booking made at 00:30+02:00 back a day, which is the same class of
        // defect the agent's own clock rules exist to prevent.
        var leaves = McpPayloads.Leaves(
            """
            [{"id": "lv-1", "employeeId": "emp-1", "leaveTypeId": "lt-1",
              "startDate": "2026-08-26T00:30:00+02:00", "endDate": "2026-08-27T23:00:00+02:00",
              "status": "approved"}]
            """);

        var leave = Assert.Single(leaves);
        Assert.Equal(new DateOnly(2026, 8, 26), leave.StartDate);
        Assert.Equal(new DateOnly(2026, 8, 27), leave.EndDate);
    }

    [Fact]
    public void A_booking_with_no_employee_is_refused_rather_than_attributed_to_the_actor()
    {
        // Defaulting this to the current user would make the only_for_self filter
        // above it pass for every row, including someone else's.
        var thrown = Assert.Throws<McpPayloadException>(() => McpPayloads.Leaves(
            """[{"id": "lv-1", "leave_type_id": "lt-1", "start_date": "2026-08-26", "end_date": "2026-08-27"}]"""));

        // The message is what the first person to run this against a real server will
        // read, so it names both what was wanted and what arrived.
        Assert.Contains("employee_id", thrown.Message, StringComparison.Ordinal);
        Assert.Contains("leave_type_id", thrown.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void A_failure_message_names_keys_and_never_values()
    {
        // The message reaches a log, and a value may be somebody's name.
        var thrown = Assert.Throws<McpPayloadException>(() => McpPayloads.User(
            """{"identifier": "emp-1", "full_name": "Robin Vale"}"""));

        Assert.Contains("identifier", thrown.Message, StringComparison.Ordinal);
        Assert.DoesNotContain("Robin Vale", thrown.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void An_absent_flag_never_makes_the_agent_more_permissive_than_the_server()
    {
        // A leave type that says nothing about approval is treated as needing it, and
        // one that says nothing about a consecutive-day limit is treated as having
        // none — which is the fixture's own meaning of zero, not a new convention.
        var type = Assert.Single(McpPayloads.LeaveTypes("""[{"id": "lt-1", "name": "Sick leave"}]"""));

        Assert.True(type.RequiresApproval);
        Assert.True(type.CountsAgainstBalance);
        Assert.False(type.AllowsHalfDays);
        Assert.Equal(0, type.MaxConsecutiveDays);
        Assert.Null(type.RequiresAttachmentAfterDays);
    }

    [Fact]
    public void A_reply_with_no_text_content_is_a_failure_and_not_an_empty_world()
    {
        // An empty list and "the server said nothing" are different facts. Reading the
        // second as the first would let a failed read look like an employee with no
        // bookings, and the conflict check would then pass on no evidence.
        Assert.Throws<McpPayloadException>(() => McpPayloads.Leaves(null));
        Assert.Throws<McpPayloadException>(() => McpPayloads.Leaves("   "));
        Assert.Throws<McpPayloadException>(() => McpPayloads.Leaves("not json"));
    }

    [Fact]
    public void A_booking_result_is_read_through_at_most_one_wrapper()
    {
        var direct = McpPayloads.TimeOff(
            """{"id": "req-1", "status": "pending", "start_date": "2026-08-26", "end_date": "2026-08-27"}""");

        var wrapped = McpPayloads.TimeOff(
            """{"leave": {"id": "req-1", "status": "pending", "start_date": "2026-08-26", "end_date": "2026-08-27"}}""");

        Assert.Equal("req-1", direct.RequestId);
        Assert.Equal(direct, wrapped);
    }
}
