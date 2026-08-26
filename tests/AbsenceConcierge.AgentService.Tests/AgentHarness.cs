using System.Diagnostics;
using AbsenceConcierge.AgentService.Agent;
using AbsenceConcierge.AgentService.Agent.Language;
using AbsenceConcierge.AgentService.Agent.Steps;
using AbsenceConcierge.AgentService.Agent.Time;
using AbsenceConcierge.AgentService.Telemetry;
using AbsenceConcierge.AgentService.Workforce;
using AbsenceConcierge.AgentService.Workforce.Confirmation;
using AbsenceConcierge.AgentService.Workforce.Fixtures;
using AbsenceConcierge.AgentService.Workforce.Mock;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using OpenTelemetry;
using OpenTelemetry.Trace;

namespace AbsenceConcierge.AgentService.Tests;

/// <summary>
/// Runs the real pipeline against the real fixture, and records the trace.
///
/// <para>
/// The trace is the point. Layer 1 of the eval harness asserts on spans and events
/// and never on prose, so these tests do the same — they are the same assertions the
/// suite will make in Phase 4, written by hand while the harness that will make them
/// automatically does not exist yet. Anything they can only check by reading the
/// reply is a behaviour Layer 1 will not be able to check either, and that is worth
/// finding out now.
/// </para>
/// <para>
/// The step list here is duplicated from
/// <c>ServiceCollectionExtensions.AddAbsenceConciergeAgent</c>, deliberately and
/// visibly: a test that built the pipeline through the container would pass even if
/// the container registered them in the wrong order, because the container would be
/// the thing under test rather than the ordering. One test below asserts the two
/// lists agree.
/// </para>
/// </summary>
public sealed class AgentHarness : IDisposable
{
    /// <summary>
    /// A span the harness opens around each turn, so everything the turn produces
    /// shares one trace id.
    ///
    /// This exists because <c>ActivitySource</c> listeners are process-global: a
    /// second <see cref="TracerProvider"/> alive at the same time receives every
    /// activity this one does, and its exporter list would contain another test's
    /// tool spans. Scoping reads to the harness's own trace ids makes each harness
    /// correct on its own, which is what the Phase 4 eval harness will need when it
    /// runs scenarios concurrently.
    /// </summary>
    private static readonly ActivitySource Scope = new(ScopeName);

    private const string ScopeName = "AbsenceConcierge.Tests.Harness";

    private readonly TracerProvider _tracer;
    private readonly List<Activity> _captured;
    private readonly HashSet<ActivityTraceId> _mine = [];

    private AgentHarness(
        TracerProvider tracer,
        List<Activity> captured,
        IAgentOrchestrator orchestrator,
        IWorkforceTools tools,
        WorkforceWorld world)
    {
        _tracer = tracer;
        _captured = captured;
        Orchestrator = orchestrator;
        Tools = tools;
        World = world;
    }

    /// <summary>Spans this harness produced, and no others.</summary>
    public IReadOnlyList<Activity> Exported =>
        [.. _captured.Where(activity =>
            _mine.Contains(activity.TraceId)
            && !string.Equals(activity.Source.Name, ScopeName, StringComparison.Ordinal))];

    public IAgentOrchestrator Orchestrator { get; }

    public IWorkforceTools Tools { get; }

    public WorkforceWorld World { get; }

    /// <summary>The step types, in the order the pipeline runs them.</summary>
    public static IReadOnlyList<Type> PipelineOrder { get; } =
    [
        typeof(EstablishActorStep),
        typeof(ConfirmationDecisionStep),
        typeof(InterpretUtteranceStep),
        typeof(ScopeGuardStep),
        typeof(ResolvePersonStep),
        typeof(ResolveDatesStep),
        typeof(LeaveTypeStep),
        typeof(ConflictCheckStep),
        typeof(DraftStep),
        typeof(ConfirmationGateStep),
        typeof(ExecuteWriteStep),
    ];

    public static AgentHarness Build(
        DateTimeOffset? now = null,
        WorkforceWorld? world = null,
        Func<IWorkforceTools, IWorkforceTools>? faults = null,
        AgentOptions? options = null)
    {
        var exported = new List<Activity>();

        var tracer = Sdk.CreateTracerProviderBuilder()
            .AddSource(AgentDiagnostics.ActivitySourceName)
            .AddSource(ScopeName)
            .AddInMemoryExporter(exported)
            .Build()!;

        world ??= TestWorld.Load();
        var agentOptions = options ?? new AgentOptions();
        var time = new FixedTimeProvider(now ?? TestWorld.Now);
        var tokens = new InMemoryConfirmationTokenStore();

        // The same zone the agent is given. Two enforcement layers computing
        // "today" in different frames is the defect this argument closes.
        IWorkforceTools inner = new MockWorkforceTools(
            world, tokens, time, AgentClock.ZoneFor(agentOptions.Timezone));

        if (faults is not null)
        {
            inner = faults(inner);
        }

        IWorkforceTools tools = new InstrumentedWorkforceTools(
            inner,
            new ToolAttemptPolicy(agentOptions.MaxReadAttempts));

        var steps = new List<IAgentStep>
        {
            new EstablishActorStep(tools),
            new ConfirmationDecisionStep(tokens),
            new InterpretUtteranceStep(new DeterministicUtteranceInterpreter()),
            new ScopeGuardStep(),
            new ResolvePersonStep(tools),
            new ResolveDatesStep(),
            new LeaveTypeStep(tools, Options.Create(agentOptions)),
            new ConflictCheckStep(tools),
            new DraftStep(),
            new ConfirmationGateStep(tokens),
            new ExecuteWriteStep(tools),
        };

        var orchestrator = new AgentOrchestrator(
            steps,
            new InMemoryAgentConversationStore(Options.Create(new AbsenceConcierge.AgentService.Demo.DemoOptions())),
            new DeterministicUtteranceInterpreter(),
            new DeterministicReplyComposer(),
            world,
            time,
            Options.Create(agentOptions),
            NullLogger<AgentOrchestrator>.Instance);

        return new AgentHarness(tracer, exported, orchestrator, tools, world);
    }

    public Task<AgentTurnResult> SayAsync(string conversationId, string message) =>
        RunAsync(AgentTurnRequest.User(conversationId, message));

    public Task<AgentTurnResult> DecideAsync(
        string conversationId,
        ConfirmationDecision decision,
        string message = "Yes") =>
        RunAsync(AgentTurnRequest.Confirmation(conversationId, message, decision));

    private async Task<AgentTurnResult> RunAsync(AgentTurnRequest request)
    {
        using var scope = Scope.StartActivity("test_turn")
            ?? throw new InvalidOperationException(
                $"The harness scope span was not sampled. The tracer provider must AddSource(\"{ScopeName}\"), "
                + "or every read of the trace below will be filtered to nothing.");

        _mine.Add(scope.TraceId);

        var result = await Orchestrator.RunTurnAsync(request);
        _tracer.ForceFlush();
        return result;
    }

    // ── Reading the trace the way a scenario reads it ────────────────────────

    public int TimesCalled(string toolName) =>
        Exported.Count(span =>
            string.Equals(
                span.GetTagItem(AgentDiagnostics.Attributes.ToolName) as string,
                toolName,
                StringComparison.Ordinal));

    public IReadOnlyList<ActivityEvent> EventsNamed(string eventName) =>
        [.. Exported.SelectMany(span => span.Events).Where(e => string.Equals(e.Name, eventName, StringComparison.Ordinal))];

    public int AttemptsOn(string toolName) =>
        Exported
            .Where(span => string.Equals(
                span.GetTagItem(AgentDiagnostics.Attributes.ToolName) as string,
                toolName,
                StringComparison.Ordinal))
            .SelectMany(span => span.Events)
            .Count(e => string.Equals(e.Name, AgentDiagnostics.Events.ToolAttempt, StringComparison.Ordinal));

    /// <summary>
    /// True when <paramref name="first"/> appears before <paramref name="second"/>
    /// in the trace, where each may be a tool name or an event name. Ordering is
    /// what most of the constraints are made of.
    /// </summary>
    public bool Ordered(string first, string second)
    {
        var firstAt = PositionOf(first);
        var secondAt = PositionOf(second);
        return firstAt >= 0 && secondAt >= 0 && firstAt < secondAt;
    }

    private int PositionOf(string toolOrEvent)
    {
        var timeline = new List<(DateTime At, string Name)>();

        foreach (var span in Exported)
        {
            if (span.GetTagItem(AgentDiagnostics.Attributes.ToolName) is string tool)
            {
                timeline.Add((span.StartTimeUtc, tool));
            }

            timeline.AddRange(span.Events.Select(e => (e.Timestamp.UtcDateTime, e.Name)));
        }

        timeline.Sort((left, right) => left.At.CompareTo(right.At));
        return timeline.FindIndex(entry => string.Equals(entry.Name, toolOrEvent, StringComparison.Ordinal));
    }

    public void Dispose() => _tracer.Dispose();
}

/// <summary>
/// Makes one tool fail, the way a scenario's <c>tool_behaviour</c> block will in
/// Phase 4. It sits <em>beneath</em> the instrumentation decorator, so a failure
/// still produces a span with attempt events — which is what the degradation
/// assertions read.
/// </summary>
public sealed class FailingWorkforceTools(IWorkforceTools inner, string toolName, ToolOutcome outcome)
    : IWorkforceTools
{
    public ValueTask<ToolResult<WorkforceUser>> GetCurrentUserAsync(CancellationToken cancellationToken = default) =>
        Fails(WorkforceToolCatalog.GetCurrentUser)
            ? ValueTask.FromResult(Fail<WorkforceUser>())
            : inner.GetCurrentUserAsync(cancellationToken);

    public ValueTask<ToolResult<IReadOnlyList<Employee>>> FindEmployeeAsync(
        string nameQuery,
        CancellationToken cancellationToken = default) =>
        Fails(WorkforceToolCatalog.FindEmployee)
            ? ValueTask.FromResult(Fail<IReadOnlyList<Employee>>())
            : inner.FindEmployeeAsync(nameQuery, cancellationToken);

    public ValueTask<ToolResult<IReadOnlyList<LeaveType>>> ListLeaveTypesAsync(
        CancellationToken cancellationToken = default) =>
        Fails(WorkforceToolCatalog.ListLeaveTypes)
            ? ValueTask.FromResult(Fail<IReadOnlyList<LeaveType>>())
            : inner.ListLeaveTypesAsync(cancellationToken);

    public ValueTask<ToolResult<IReadOnlyList<Leave>>> ListLeavesAsync(
        CancellationToken cancellationToken = default) =>
        Fails(WorkforceToolCatalog.ListLeaves)
            ? ValueTask.FromResult(Fail<IReadOnlyList<Leave>>())
            : inner.ListLeavesAsync(cancellationToken);

    public ValueTask<ToolResult<TimeOffResult>> RequestTimeOffAsync(
        TimeOffRequest request,
        CancellationToken cancellationToken = default) =>
        Fails(WorkforceToolCatalog.RequestTimeOff)
            ? ValueTask.FromResult(Fail<TimeOffResult>())
            : inner.RequestTimeOffAsync(request, cancellationToken);

    private bool Fails(string tool) => string.Equals(tool, toolName, StringComparison.Ordinal);

    private ToolResult<T> Fail<T>() => outcome switch
    {
        ToolOutcome.Indeterminate => ToolResult<T>.Indeterminate("The request timed out."),
        ToolOutcome.Success => ToolResult<T>.Ok(default!),
        _ => ToolResult<T>.Failed("The backend returned an error."),
    };
}

/// <summary>Returns an empty list from one read, which is a success that answered nothing.</summary>
public sealed class EmptyLeaveTypesWorkforceTools(IWorkforceTools inner) : IWorkforceTools
{
    public ValueTask<ToolResult<WorkforceUser>> GetCurrentUserAsync(CancellationToken cancellationToken = default) =>
        inner.GetCurrentUserAsync(cancellationToken);

    public ValueTask<ToolResult<IReadOnlyList<Employee>>> FindEmployeeAsync(
        string nameQuery,
        CancellationToken cancellationToken = default) =>
        inner.FindEmployeeAsync(nameQuery, cancellationToken);

    public ValueTask<ToolResult<IReadOnlyList<LeaveType>>> ListLeaveTypesAsync(
        CancellationToken cancellationToken = default) =>
        ValueTask.FromResult(ToolResult<IReadOnlyList<LeaveType>>.Ok([]));

    public ValueTask<ToolResult<IReadOnlyList<Leave>>> ListLeavesAsync(
        CancellationToken cancellationToken = default) =>
        inner.ListLeavesAsync(cancellationToken);

    public ValueTask<ToolResult<TimeOffResult>> RequestTimeOffAsync(
        TimeOffRequest request,
        CancellationToken cancellationToken = default) =>
        inner.RequestTimeOffAsync(request, cancellationToken);
}
