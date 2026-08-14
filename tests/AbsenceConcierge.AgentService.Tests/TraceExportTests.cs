using System.Diagnostics;
using AbsenceConcierge.AgentService.Telemetry;
using AbsenceConcierge.AgentService.Workforce;
using OpenTelemetry;
using OpenTelemetry.Trace;

namespace AbsenceConcierge.AgentService.Tests;

/// <summary>
/// Phase 2's headline claim, tested: a tool call produces a span, and that span
/// reaches an exporter.
///
/// This matters more than it looks. Layer 1 of the eval harness asserts over the
/// trace and nothing else — if the trace is empty, incomplete, or shaped differently
/// from what a scenario expects, every scenario fails for a reason that has nothing
/// to do with the agent. So the trace is not diagnostics here; it is the interface
/// the whole suite is written against, and it gets tested like one.
///
/// The exporter is in-memory rather than OTLP on purpose: the assertion is that the
/// span reaches an exporter, not that a collector is running. That is also how the
/// harness will read traces in Phase 4 — in-process, with no endpoint configured,
/// which is what lets the suite run on a fresh clone with zero credentials.
/// </summary>
public sealed class TraceExportTests
{
    private static (TracerProvider Provider, List<Activity> Exported) StartRecording()
    {
        var exported = new List<Activity>();

        var provider = Sdk.CreateTracerProviderBuilder()
            .AddSource(AgentDiagnostics.ActivitySourceName)
            .AddInMemoryExporter(exported)
            .Build();

        return (provider!, exported);
    }

    [Fact]
    public async Task A_read_tool_call_is_exported_as_a_span_with_its_classification()
    {
        var (provider, exported) = StartRecording();
        using (provider)
        {
            var (mock, _, _) = TestWorld.Build();
            IWorkforceTools tools = new InstrumentedWorkforceTools(mock);

            var result = await tools.ListLeaveTypesAsync();
            provider.ForceFlush();

            Assert.Equal(ToolOutcome.Success, result.Outcome);

            var span = Assert.Single(exported, a => a.DisplayName == "execute_tool list_leave_types");
            Assert.Equal("list_leave_types", span.GetTagItem(AgentDiagnostics.Attributes.ToolName));
            Assert.Equal("execute_tool", span.GetTagItem(AgentDiagnostics.Attributes.OperationName));
            Assert.Equal("read", span.GetTagItem(AgentDiagnostics.Attributes.ToolKind));
            Assert.Equal("success", span.GetTagItem(AgentDiagnostics.Attributes.ToolOutcome));
            Assert.Equal(ActivityStatusCode.Unset, span.Status);
        }
    }

    [Fact]
    public async Task A_write_tool_call_is_classified_as_a_write_in_the_trace()
    {
        // C-1 is "no write-classified span before a confirmation event". That
        // assertion is only possible if the span says which it is — and says so from
        // the catalogue rather than from its name.
        var (provider, exported) = StartRecording();
        using (provider)
        {
            var (mock, tokens, _) = TestWorld.Build();
            IWorkforceTools tools = new InstrumentedWorkforceTools(mock);
            var start = new DateOnly(2026, 8, 26);
            var token = TestWorld.ApprovedToken(tokens, TestWorld.VacationTypeId, start, start);

            var result = await tools.RequestTimeOffAsync(
                new TimeOffRequest(TestWorld.VacationTypeId, start, start, token));
            provider.ForceFlush();

            Assert.Equal(ToolOutcome.Success, result.Outcome);

            var span = Assert.Single(exported, a => a.DisplayName == "execute_tool request_time_off");
            Assert.Equal("write", span.GetTagItem(AgentDiagnostics.Attributes.ToolKind));
        }
    }

    [Fact]
    public async Task A_refused_write_is_exported_as_an_error_with_its_outcome()
    {
        // A trace that renders a refusal identically to a success makes a denied path
        // indistinguishable from a happy one, and the eval harness reads the trace.
        var (provider, exported) = StartRecording();
        using (provider)
        {
            var (mock, _, _) = TestWorld.Build();
            IWorkforceTools tools = new InstrumentedWorkforceTools(mock);
            var start = new DateOnly(2026, 8, 26);

            var result = await tools.RequestTimeOffAsync(
                new TimeOffRequest(TestWorld.VacationTypeId, start, start, ConfirmationToken: string.Empty));
            provider.ForceFlush();

            Assert.Equal(ToolOutcome.ConfirmationRequired, result.Outcome);

            var span = Assert.Single(exported, a => a.DisplayName == "execute_tool request_time_off");
            Assert.Equal("confirmationrequired", span.GetTagItem(AgentDiagnostics.Attributes.ToolOutcome));
            Assert.Equal(ActivityStatusCode.Error, span.Status);
        }
    }

    [Fact]
    public async Task The_confirmation_token_never_reaches_the_trace()
    {
        // The token authorises a write, which makes it a credential. Traces get
        // exported to places the agent does not control, and a credential recorded in
        // a span is a credential disclosed (P5).
        var (provider, exported) = StartRecording();
        using (provider)
        {
            var (mock, tokens, _) = TestWorld.Build();
            IWorkforceTools tools = new InstrumentedWorkforceTools(mock);
            var start = new DateOnly(2026, 8, 26);
            var token = TestWorld.ApprovedToken(tokens, TestWorld.VacationTypeId, start, start);

            await tools.RequestTimeOffAsync(new TimeOffRequest(TestWorld.VacationTypeId, start, start, token));
            provider.ForceFlush();

            var span = Assert.Single(exported, a => a.DisplayName == "execute_tool request_time_off");
            var arguments = span.GetTagItem(AgentDiagnostics.Attributes.ToolArguments) as string;

            Assert.NotNull(arguments);
            Assert.DoesNotContain(token, arguments!, StringComparison.Ordinal);
            // …while the arguments a scenario asserts on are present.
            Assert.Contains("leave_type_id=lt-201", arguments!, StringComparison.Ordinal);
            Assert.Contains("start_date=2026-08-26", arguments!, StringComparison.Ordinal);
        }
    }

    [Fact]
    public async Task Every_tool_on_the_catalogue_produces_a_span()
    {
        // Guards against the failure where instrumentation is added per-method and one
        // method is missed — the decorator exists so this cannot happen, and this test
        // is what proves the decorator actually covers the surface.
        var (provider, exported) = StartRecording();
        using (provider)
        {
            var (mock, _, _) = TestWorld.Build();
            IWorkforceTools tools = new InstrumentedWorkforceTools(mock);

            await tools.GetCurrentUserAsync();
            await tools.FindEmployeeAsync("Sam");
            await tools.ListLeaveTypesAsync();
            await tools.ListLeavesAsync();
            await tools.RequestTimeOffAsync(
                new TimeOffRequest(TestWorld.VacationTypeId, new DateOnly(2026, 8, 26), new DateOnly(2026, 8, 26), string.Empty));
            provider.ForceFlush();

            var traced = exported.Select(a => a.GetTagItem(AgentDiagnostics.Attributes.ToolName) as string).ToHashSet();

            Assert.Equal(WorkforceToolCatalog.Names.ToHashSet(), traced);
        }
    }
}
