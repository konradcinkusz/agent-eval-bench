using System.Diagnostics;
using AbsenceConcierge.AgentService.Telemetry;
using AbsenceConcierge.AgentService.Workforce;
using AbsenceConcierge.AgentService.Workforce.Confirmation;
using AbsenceConcierge.AgentService.Workforce.Mcp;
using Microsoft.Extensions.Logging.Abstractions;
using OpenTelemetry;
using OpenTelemetry.Trace;

namespace AbsenceConcierge.AgentService.Tests;

/// <summary>
/// The Model Context Protocol adapter, tested against a fake session.
///
/// <para>
/// These tests carry more weight than usual, because this repository has no server to
/// point the adapter at (docs/DEVIATIONS.md D-10). What they establish is the part
/// that does not depend on a server existing: that the confirmation gate is enforced
/// on this path too, that only_for_self survives a server that ignores it, that a
/// failure is classified into the three answers SPEC §7.2 distinguishes, and that a
/// call produces the same span the mock's calls produce.
/// </para>
/// <para>
/// What they cannot establish is that a real server's payloads look like these. That
/// gap is named rather than papered over — which is why the payload tests below are
/// written as "this shape is accepted" rather than "this is the shape".
/// </para>
/// </summary>
public sealed class McpAdapterTests
{
    private const string ActorId = "emp-100";
    private const string ColleagueId = "emp-200";
    private const string SickTypeId = "lt-sick";

    private static readonly DateOnly Start = new(2026, 8, 26);
    private static readonly DateOnly End = new(2026, 8, 27);

    private const string ActorJson = """
        {"employee_id": "emp-100", "display_name": "Robin Vale", "team": "Platform",
         "permissions": ["directory:read", "timeoff:read", "timeoff:request"]}
        """;

    private const string BookedJson = """
        {"request_id": "req-1", "status": "pending_approval",
         "start_date": "2026-08-26", "end_date": "2026-08-27"}
        """;

    // ── The gate ────────────────────────────────────────────────────────────────

    [Fact]
    public async Task An_unconfirmed_write_is_refused_here_and_never_reaches_the_server()
    {
        var session = new FakeMcpToolSession().AnsweringJson(WorkforceToolCatalog.GetCurrentUser, ActorJson);
        var (tools, _) = Build(session);

        var result = await tools.RequestTimeOffAsync(
            new TimeOffRequest(SickTypeId, Start, End, ConfirmationToken: string.Empty));

        // Two assertions, because "it was refused" and "nothing happened" are
        // different claims and a denied path has to make both.
        Assert.Equal(ToolOutcome.ConfirmationRequired, result.Outcome);
        Assert.Equal(0, session.CallsTo(WorkforceToolCatalog.RequestTimeOff));
    }

    [Fact]
    public async Task An_approved_token_authorises_exactly_one_remote_write()
    {
        var session = new FakeMcpToolSession()
            .AnsweringJson(WorkforceToolCatalog.GetCurrentUser, ActorJson)
            .AnsweringJson(WorkforceToolCatalog.RequestTimeOff, BookedJson);

        var (tools, tokens) = Build(session);
        var token = ApprovedToken(tokens, ActorId);

        var first = await tools.RequestTimeOffAsync(new TimeOffRequest(SickTypeId, Start, End, token));
        var second = await tools.RequestTimeOffAsync(new TimeOffRequest(SickTypeId, Start, End, token));

        Assert.Equal(ToolOutcome.Success, first.Outcome);
        Assert.Equal("req-1", first.Value?.RequestId);
        Assert.Equal(ToolOutcome.ConfirmationRequired, second.Outcome);
        Assert.Equal(1, session.CallsTo(WorkforceToolCatalog.RequestTimeOff));
    }

    [Fact]
    public async Task The_token_is_bound_to_the_employee_the_server_reports()
    {
        // The request carries no employee id, and that is deliberate: if the id came
        // from the arguments, an instruction that changed whose leave this is would
        // also change what the approval appeared to cover. It comes from
        // get_current_user, so a token minted for anyone else does not fit.
        var session = new FakeMcpToolSession()
            .AnsweringJson(WorkforceToolCatalog.GetCurrentUser, ActorJson)
            .AnsweringJson(WorkforceToolCatalog.RequestTimeOff, BookedJson);

        var (tools, tokens) = Build(session);
        var token = ApprovedToken(tokens, ColleagueId);

        var result = await tools.RequestTimeOffAsync(new TimeOffRequest(SickTypeId, Start, End, token));

        Assert.Equal(ToolOutcome.ConfirmationRequired, result.Outcome);
        Assert.Equal(0, session.CallsTo(WorkforceToolCatalog.RequestTimeOff));
    }

    [Fact]
    public async Task An_indeterminate_write_leaves_nothing_to_retry_with()
    {
        // C-6 through a failure. The token is spent before the request goes out, so
        // "we do not know whether that booked" cannot be resolved by trying again —
        // which is the correct outcome, because trying again is how one approval
        // becomes two days of leave.
        var session = new FakeMcpToolSession()
            .AnsweringJson(WorkforceToolCatalog.GetCurrentUser, ActorJson)
            .AnsweringInTurn(
                WorkforceToolCatalog.RequestTimeOff,
                McpToolReply.Unknown("no answer within 30s"),
                McpToolReply.Ok(BookedJson));

        var (tools, tokens) = Build(session);
        var token = ApprovedToken(tokens, ActorId);

        var first = await tools.RequestTimeOffAsync(new TimeOffRequest(SickTypeId, Start, End, token));
        var retry = await tools.RequestTimeOffAsync(new TimeOffRequest(SickTypeId, Start, End, token));

        Assert.Equal(ToolOutcome.Indeterminate, first.Outcome);
        Assert.Equal(ToolOutcome.ConfirmationRequired, retry.Outcome);
        Assert.Equal(1, session.CallsTo(WorkforceToolCatalog.RequestTimeOff));
    }

    // ── The three answers a failure can have ────────────────────────────────────

    [Fact]
    public async Task A_write_that_never_left_this_machine_is_a_definite_failure()
    {
        // The distinction the session's catch blocks exist to draw. A refused
        // connection is knowledge: nothing was booked. Reporting that as "it may have
        // happened" would tell a sick employee to go and check a system that has
        // nothing in it.
        var session = new FakeMcpToolSession()
            .AnsweringJson(WorkforceToolCatalog.GetCurrentUser, ActorJson)
            .Answering(WorkforceToolCatalog.RequestTimeOff, McpToolReply.Transport("connection refused"));

        var (tools, tokens) = Build(session);
        var token = ApprovedToken(tokens, ActorId);

        var result = await tools.RequestTimeOffAsync(new TimeOffRequest(SickTypeId, Start, End, token));

        Assert.Equal(ToolOutcome.Failed, result.Outcome);
    }

    [Fact]
    public async Task A_read_that_may_or_may_not_have_run_is_still_only_a_failure()
    {
        // "It may have happened" is a sentence about a write. A read that timed out
        // produced no data, and the agent's degradation path already covers that.
        var session = new FakeMcpToolSession()
            .AnsweringJson(WorkforceToolCatalog.GetCurrentUser, ActorJson)
            .Answering(WorkforceToolCatalog.ListLeaves, McpToolReply.Unknown("no answer within 30s"));

        var (tools, _) = Build(session);

        var result = await tools.ListLeavesAsync();

        Assert.Equal(ToolOutcome.Failed, result.Outcome);
    }

    [Fact]
    public async Task A_write_the_server_declined_definitely_did_not_happen()
    {
        var session = new FakeMcpToolSession()
            .AnsweringJson(WorkforceToolCatalog.GetCurrentUser, ActorJson)
            .Answering(WorkforceToolCatalog.RequestTimeOff, McpToolReply.ToolError("leave_type_id is not valid"));

        var (tools, tokens) = Build(session);
        var token = ApprovedToken(tokens, ActorId);

        var result = await tools.RequestTimeOffAsync(new TimeOffRequest(SickTypeId, Start, End, token));

        Assert.Equal(ToolOutcome.Rejected, result.Outcome);
    }

    [Fact]
    public async Task A_servers_error_text_stays_in_the_log_and_out_of_the_result()
    {
        // The result's message becomes the span's status description, and a span gets
        // exported. Remote free text may name a person; it does not go there.
        var session = new FakeMcpToolSession()
            .AnsweringJson(WorkforceToolCatalog.GetCurrentUser, ActorJson)
            .Answering(WorkforceToolCatalog.ListLeaves, McpToolReply.ToolError("Alex Moreau is not visible to you"));

        var (tools, _) = Build(session);

        var result = await tools.ListLeavesAsync();

        Assert.Equal(ToolOutcome.Failed, result.Outcome);
        Assert.NotNull(result.Message);
        Assert.DoesNotContain("Alex Moreau", result.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_declined_call_becomes_a_permission_denial_only_where_a_deployment_says_so()
    {
        // Guessing which of a foreign system's messages mean "not allowed" is
        // string-matching prose that the next release rewords. The default makes no
        // claim; a deployment that knows its server can.
        var text = "403: insufficient_scope for time_off.write";

        var guessing = new FakeMcpToolSession()
            .AnsweringJson(WorkforceToolCatalog.GetCurrentUser, ActorJson)
            .Answering(WorkforceToolCatalog.ListLeaveTypes, McpToolReply.ToolError(text));

        var (defaults, _) = Build(guessing);
        var withoutMarkers = await defaults.ListLeaveTypesAsync();

        var configured = new FakeMcpToolSession()
            .AnsweringJson(WorkforceToolCatalog.GetCurrentUser, ActorJson)
            .Answering(WorkforceToolCatalog.ListLeaveTypes, McpToolReply.ToolError(text));

        var options = new McpOptions();
        options.PermissionDeniedMarkers.Add("insufficient_scope");
        var (told, _) = Build(configured, options);
        var withMarkers = await told.ListLeaveTypesAsync();

        Assert.Equal(ToolOutcome.Failed, withoutMarkers.Outcome);
        Assert.Equal(ToolOutcome.PermissionDenied, withMarkers.Outcome);
    }

    // ── only_for_self ───────────────────────────────────────────────────────────

    [Fact]
    public async Task A_colleagues_booking_does_not_survive_a_server_that_ignores_the_filter()
    {
        var session = new FakeMcpToolSession()
            .AnsweringJson(WorkforceToolCatalog.GetCurrentUser, ActorJson)
            .AnsweringJson(
                WorkforceToolCatalog.ListLeaves,
                """
                [{"id": "lv-1", "employee_id": "emp-100", "leave_type_id": "lt-sick",
                  "start_date": "2026-08-26", "end_date": "2026-08-27", "status": "approved"},
                 {"id": "lv-2", "employee_id": "emp-200", "leave_type_id": "lt-holiday",
                  "start_date": "2026-09-01", "end_date": "2026-09-05", "status": "approved"}]
                """);

        var (tools, _) = Build(session);

        var result = await tools.ListLeavesAsync();

        var leave = Assert.Single(result.Value!);
        Assert.Equal("lv-1", leave.Id);
        Assert.Equal(ActorId, (string?)session.LastArgumentsTo(WorkforceToolCatalog.ListLeaves)["employee_id"]);
    }

    // ── The boundary's vocabulary ───────────────────────────────────────────────

    [Fact]
    public async Task The_remote_tool_name_comes_from_configuration()
    {
        // P11 in one assertion: a foreign system's names are absorbed at the boundary
        // and nothing above it changes.
        var options = new McpOptions();
        options.ToolNames.ListLeaveTypes = "hr.absence.policies";

        var session = new FakeMcpToolSession().AnsweringJson("hr.absence.policies", """[]""");
        var (tools, _) = Build(session, options);

        var result = await tools.ListLeaveTypesAsync();

        Assert.Equal(ToolOutcome.Success, result.Outcome);
        Assert.Equal(1, session.CallsTo("hr.absence.policies"));
    }

    [Fact]
    public void A_tool_outside_the_catalogue_is_not_passed_through_to_the_server()
    {
        // A tool nobody classified read or write is a tool C-1 cannot be derived from.
        Assert.Throws<ArgumentOutOfRangeException>(() => new McpToolNames().For("delete_everything"));
    }

    [Fact]
    public async Task Without_a_scope_map_the_agent_stops_pre_empting_the_servers_refusals()
    {
        // The alternative — an empty permission set — makes every request fail with a
        // confident "you are not allowed", which is a wrong answer in the tone of a
        // right one. The server is the authority in this mode; the agent defers.
        var session = new FakeMcpToolSession().AnsweringJson(
            WorkforceToolCatalog.GetCurrentUser,
            """{"employee_id": "emp-100", "display_name": "Robin Vale", "permissions": ["hr:absences:write"]}""");

        var (tools, _) = Build(session);

        var result = await tools.GetCurrentUserAsync();

        Assert.Equal(WorkforceToolCatalog.AllPermissions, result.Value?.Permissions);
    }

    [Fact]
    public async Task A_configured_scope_map_translates_and_drops_what_it_does_not_know()
    {
        var options = new McpOptions();
        options.PermissionScopes["hr:absences:write"] = Permissions.TimeOffRequest;
        options.PermissionScopes["hr:absences:read"] = Permissions.TimeOffRead;

        var session = new FakeMcpToolSession().AnsweringJson(
            WorkforceToolCatalog.GetCurrentUser,
            """
            {"employee_id": "emp-100", "display_name": "Robin Vale",
             "permissions": ["hr:absences:read", "hr:payroll:read"]}
            """);

        var (tools, _) = Build(session, options);

        var result = await tools.GetCurrentUserAsync();

        // Payroll is out of scope for this agent and stays out: an unmapped scope is
        // dropped rather than passed through as a permission nothing understands.
        Assert.Equal(new[] { Permissions.TimeOffRead }, result.Value?.Permissions);
    }

    // ── Mode parity ─────────────────────────────────────────────────────────────

    [Fact]
    public async Task A_remote_write_produces_the_same_span_a_mock_write_produces()
    {
        // The claim the eval suite rests on. Every scenario runs against the mock, and
        // the evidence that says anything at all about MCP mode is that both go
        // through WorkforceToolsFactory and therefore emit one span, classified from
        // the catalogue, with the confirmation token absent from its arguments.
        var exported = new List<Activity>();

        using var provider = Sdk.CreateTracerProviderBuilder()
            .AddSource(AgentDiagnostics.ActivitySourceName)
            .AddInMemoryExporter(exported)
            .Build()!;

        var session = new FakeMcpToolSession()
            .AnsweringJson(WorkforceToolCatalog.GetCurrentUser, ActorJson)
            .AnsweringJson(WorkforceToolCatalog.RequestTimeOff, BookedJson);

        var tokens = new InMemoryConfirmationTokenStore();
        var adapter = new McpWorkforceTools(session, new McpOptions(), tokens, NullLogger<McpWorkforceTools>.Instance);
        var tools = WorkforceToolsFactory.Instrument(adapter, maxReadAttempts: 2);

        var token = ApprovedToken(tokens, ActorId);
        var result = await tools.RequestTimeOffAsync(new TimeOffRequest(SickTypeId, Start, End, token));
        provider.ForceFlush();

        Assert.Equal(ToolOutcome.Success, result.Outcome);

        var span = Assert.Single(exported, activity => activity.DisplayName == "execute_tool request_time_off");
        Assert.Equal("write", span.GetTagItem(AgentDiagnostics.Attributes.ToolKind));
        Assert.Equal("success", span.GetTagItem(AgentDiagnostics.Attributes.ToolOutcome));
        Assert.DoesNotContain(
            token,
            (string)span.GetTagItem(AgentDiagnostics.Attributes.ToolArguments)!,
            StringComparison.Ordinal);
    }

    // ── Helpers ─────────────────────────────────────────────────────────────────

    private static (McpWorkforceTools Tools, IConfirmationTokenStore Tokens) Build(
        FakeMcpToolSession session,
        McpOptions? options = null)
    {
        var tokens = new InMemoryConfirmationTokenStore();

        var tools = new McpWorkforceTools(
            session,
            options ?? new McpOptions(),
            tokens,
            NullLogger<McpWorkforceTools>.Instance);

        return (tools, tokens);
    }

    private static string ApprovedToken(IConfirmationTokenStore tokens, string employeeId)
    {
        var token = tokens.Issue(new ConfirmationDraft(employeeId, SickTypeId, Start, End));
        tokens.Approve(token);
        return token;
    }
}
