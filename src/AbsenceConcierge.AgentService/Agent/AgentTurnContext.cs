using System.Diagnostics;
using AbsenceConcierge.AgentService.Agent.Language;
using AbsenceConcierge.AgentService.Agent.Time;
using AbsenceConcierge.AgentService.Telemetry;
using AbsenceConcierge.AgentService.Workforce;

namespace AbsenceConcierge.AgentService.Agent;

/// <summary>A phase that produced partial or no data, as SPEC §2.2 requires it recorded.</summary>
public sealed record DegradationNote(string Phase, string Tool, string Kind);

/// <summary>
/// Everything one turn knows, as it learns it.
///
/// <para>
/// The steps read and write this and nothing else, which is what makes the pipeline
/// a pipeline rather than eight classes with references to each other. It is
/// deliberately mutable and deliberately not shared beyond the turn.
/// </para>
/// <para>
/// <b>Note what is <em>not</em> here: any free text from a tool result that a step
/// branches on.</b> Display names, leave-type names and comments are carried
/// through to the reply as data and are never read as instructions. That is the
/// structural half of C-7; <c>injection.ignored</c> is only the reporting half.
/// </para>
/// </summary>
public sealed class AgentTurnContext(
    AgentTurnRequest request,
    AgentConversation conversation,
    AgentClock clock,
    WorkingCalendar calendar,
    Activity? turnActivity)
{
    private readonly List<DegradationNote> _degradations = [];

    public AgentTurnRequest Request => request;

    public AgentConversation Conversation => conversation;

    public AgentClock Clock => clock;

    public WorkingCalendar Calendar => calendar;

    public TurnOutcomeRecorder Outcomes { get; } = new();

    // ── What the turn has established, in the order the pipeline establishes it ──

    public WorkforceUser? Actor { get; set; }

    public Intent? Intent { get; set; }

    /// <summary>Set when the human's decision on a held draft is being processed.</summary>
    public LeaveDraft? ApprovedDraft { get; set; }

    public string? ApprovedToken { get; set; }

    public DateResolution? Dates { get; set; }

    public IReadOnlyList<LeaveType> LeaveTypes { get; set; } = [];

    public LeaveType? SelectedLeaveType { get; set; }

    public IReadOnlyList<Leave> ConflictingLeaves { get; set; } = [];

    public string ConflictCheck { get; set; } = AgentDiagnostics.ConflictCheckStates.NotRun;

    public IReadOnlyList<Employee> EmployeeMatches { get; set; } = [];

    public LeaveDraft? Draft { get; set; }

    public ToolResult<TimeOffResult>? WriteResult { get; set; }

    // ── Why the turn ended the way it did, for the reply and for the trace ──

    public string? RefusalRule { get; set; }

    public string? ClarificationReason { get; set; }

    public IReadOnlyList<DegradationNote> Degradations => _degradations;

    /// <summary>
    /// Records a refusal: the event, the rule it rests on, and the outcome. One call
    /// so the three cannot drift apart — a refusal that sets the outcome without the
    /// event passes half the two-assertion rule and fails the other half for a
    /// reason nobody can find (SPEC §4).
    /// </summary>
    public void Refuse(string rule)
    {
        RefusalRule = rule;
        EmitEvent(AgentDiagnostics.Events.RefusalIssued, (AgentDiagnostics.Attributes.RefusalRule, rule));
        Outcomes.Record(AgentDiagnostics.TurnOutcomes.Refused);
    }

    /// <summary>Records a clarifying question: the event, its reason, and the outcome.</summary>
    public void AskFor(string reason)
    {
        ClarificationReason = reason;
        EmitEvent(
            AgentDiagnostics.Events.ClarificationRequested,
            (AgentDiagnostics.Attributes.ClarificationReason, reason));
        Outcomes.Record(AgentDiagnostics.TurnOutcomes.ClarificationRequested);
    }

    /// <summary>
    /// Records that a phase produced partial or no data. The outcome is set here
    /// too, because a note without the outcome is a degradation the trace does not
    /// rank, and §2.3's precedence exists so this turn cannot report as routine.
    /// </summary>
    public void NoteDegradation(string phase, string tool, string kind)
    {
        _degradations.Add(new DegradationNote(phase, tool, kind));

        EmitEvent(
            AgentDiagnostics.Events.DegradationNoted,
            (AgentDiagnostics.Attributes.DegradationPhase, phase),
            (AgentDiagnostics.Attributes.DegradationTool, tool),
            (AgentDiagnostics.Attributes.DegradationKind, kind));

        Outcomes.Record(AgentDiagnostics.TurnOutcomes.Degraded);
    }

    /// <summary>
    /// Records instruction-shaped content that was found and not followed. Emitted
    /// once per finding so a scenario can assert on the source as well as the fact.
    /// </summary>
    public void NoteIgnoredInstruction(InstructionShapedFinding finding)
    {
        ArgumentNullException.ThrowIfNull(finding);

        EmitEvent(
            AgentDiagnostics.Events.InjectionIgnored,
            (AgentDiagnostics.Attributes.InjectionSource, finding.Source),
            (AgentDiagnostics.Attributes.InjectionTool, finding.Tool),
            (AgentDiagnostics.Attributes.InjectionField, finding.Field),
            (AgentDiagnostics.Attributes.InjectionSignal, finding.Signal));
    }

    /// <summary>
    /// Adds an event to the turn span. Events, not logs: Layer 1 reads the trace and
    /// only the trace, so an event that exists nowhere but a log line is a behaviour
    /// no scenario can assert (SPEC §2.2).
    /// </summary>
    public void EmitEvent(string name, params (string Key, object? Value)[] tags)
    {
        if (turnActivity is null)
        {
            return;
        }

        var collection = new ActivityTagsCollection();

        foreach (var (key, value) in tags)
        {
            if (value is not null)
            {
                collection[key] = value;
            }
        }

        turnActivity.AddEvent(new ActivityEvent(name, tags: collection));
    }

    public void SetTurnTag(string key, object? value) => turnActivity?.SetTag(key, value);
}
