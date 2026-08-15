using System.Diagnostics;

namespace AbsenceConcierge.AgentService.Telemetry;

/// <summary>
/// The agent's trace vocabulary, in one place.
///
/// This is the contract the eval harness asserts against, not diagnostics
/// (<c>docs/SPEC.md</c> §2.2). Layer 1 reads these names and never the agent's
/// prose, so renaming a constant here is a breaking change to the eval suite and
/// is reviewed as one.
///
/// It lives in the service rather than in ServiceDefaults because it is domain
/// vocabulary — <c>confirmation.shown</c> means nothing to a service that does not
/// book leave. The kernel stays plumbing (P2).
/// </summary>
public static class AgentDiagnostics
{
    /// <summary>
    /// Also the OTel service name. The eval harness subscribes to this source to
    /// capture spans in-process, with no collector and no exporter configured —
    /// which is what lets the whole suite run on a fresh clone.
    /// </summary>
    public const string ActivitySourceName = "AbsenceConcierge.Agent";

    public static readonly ActivitySource Source = new(ActivitySourceName);

    /// <summary>Trace events that are part of the contract (SPEC §2.2).</summary>
    public static class Events
    {
        public const string ConfirmationShown = "confirmation.shown";
        public const string ConfirmationReceived = "confirmation.received";
        public const string ConfirmationRejected = "confirmation.rejected";
        public const string ClarificationRequested = "clarification.requested";
        public const string RefusalIssued = "refusal.issued";
        public const string DegradationNoted = "degradation.noted";
        public const string InjectionIgnored = "injection.ignored";

        /// <summary>
        /// One transport attempt inside a logical tool call. Attempts are events on
        /// the tool's span, never sibling spans — SPEC §2.2.1, which exists because
        /// "called once" is undefined otherwise.
        /// </summary>
        public const string ToolAttempt = "attempt";
    }

    /// <summary>
    /// Span and event attribute names.
    ///
    /// The <c>gen_ai.*</c> names follow the OpenTelemetry GenAI semantic conventions
    /// so that a production trace and an offline scenario share one vocabulary and a
    /// live failure replays as a scenario by extraction rather than authorship. The
    /// <c>workforce.*</c> and <c>agent.*</c> names are this repository's own, for
    /// concepts the conventions do not cover.
    /// </summary>
    public static class Attributes
    {
        public const string ToolName = "gen_ai.tool.name";
        public const string OperationName = "gen_ai.operation.name";
        public const string AgentName = "gen_ai.agent.name";

        /// <summary>
        /// Which <c>IUtteranceInterpreter</c> read the user's words. Recorded on
        /// every turn span because a baseline gathered under one interpreter does
        /// not describe the other, and a suite that mixed them silently would be
        /// measuring with a ruler that changes length (ADR-0004).
        /// </summary>
        public const string Interpreter = "agent.interpreter";

        /// <summary>The step that produced this span, from the pipeline's own list.</summary>
        public const string StepName = "agent.step.name";

        /// <summary>Whether the step ran or declined the turn. Both are information.</summary>
        public const string StepApplied = "agent.step.applied";

        /// <summary>How many steps the turn consumed, against the cap C-4 forbids reaching.</summary>
        public const string Iterations = "agent.iterations";

        /// <summary>read | write — from the catalogue, never inferred from the name.</summary>
        public const string ToolKind = "workforce.tool.kind";
        public const string ToolOutcome = "workforce.tool.outcome";
        public const string ToolArguments = "workforce.tool.arguments";

        /// <summary>
        /// The identifiers the call returned, semicolon-separated. Without it C-5
        /// is a constraint the trace cannot answer: grounding asks whether a written
        /// id came from an earlier tool result, and nothing recorded what a result
        /// contained (SPEC §2.2).
        /// </summary>
        public const string ToolResultIds = "workforce.tool.result_ids";

        public const string TurnOutcome = "agent.turn.outcome";
        public const string TerminationReason = "agent.termination.reason";

        /// <summary>1-based. Scenarios assert on <c>turn: 1</c> and <c>turn: last</c>.</summary>
        public const string TurnIndex = "agent.turn.index";

        public const string ConfirmationEmployeeId = "confirmation.employee_id";
        public const string ConfirmationLeaveTypeId = "confirmation.leave_type_id";
        public const string ConfirmationLeaveTypeName = "confirmation.leave_type_name";
        public const string ConfirmationStartDate = "confirmation.start_date";
        public const string ConfirmationEndDate = "confirmation.end_date";
        public const string ConfirmationWorkingDays = "confirmation.working_days";
        public const string ConfirmationExcludedDays = "confirmation.excluded_days";
        public const string ConfirmationAttachmentRequired = "confirmation.attachment_required";
        public const string ConfirmationConflictCheck = "confirmation.conflict_check";

        public const string DegradationPhase = "degradation.phase";
        public const string DegradationTool = "degradation.tool";
        public const string DegradationKind = "degradation.kind";

        /// <summary>Why the agent asked instead of proceeding. One of <see cref="ClarificationReasons"/>.</summary>
        public const string ClarificationReason = "clarification.reason";

        /// <summary>Which rule of SPEC §6 the refusal rests on — <c>O-1</c> … <c>O-7</c>.</summary>
        public const string RefusalRule = "refusal.rule";

        /// <summary><c>user_input</c> or <c>tool_result</c>.</summary>
        public const string InjectionSource = "injection.source";

        /// <summary>The tool whose result carried it, when it came from one.</summary>
        public const string InjectionTool = "injection.tool";

        /// <summary>The field it arrived in — a display name, a leave-type name, a comment.</summary>
        public const string InjectionField = "injection.field";

        /// <summary>Which class of instruction was recognised.</summary>
        public const string InjectionSignal = "injection.signal";

        /// <summary>Attempt events on a tool span (SPEC §2.2.1).</summary>
        public const string AttemptNumber = "attempt.number";

        public const string AttemptOutcome = "attempt.outcome";
    }

    /// <summary>
    /// Why the agent stopped to ask. These are trace values, not prose: B-12 and
    /// B-13 are about the agent asking rather than guessing, and a scenario that had
    /// to match the question's wording would start grading phrasing (ADR-0003).
    /// </summary>
    public static class ClarificationReasons
    {
        public const string AmbiguousDate = "ambiguous_date";
        public const string NoDateGiven = "no_date_given";
        public const string DatesFromAnotherPerson = "dates_from_another_person";
        public const string NonWorkingDay = "non_working_day";
        public const string DateInThePast = "date_in_the_past";
        public const string AmbiguousEmployee = "ambiguous_employee";
        public const string NoMatchingLeaveType = "no_matching_leave_type";
        public const string ConflictingBooking = "conflicting_booking";
        public const string NothingRequested = "nothing_requested";
    }

    /// <summary>The phases of SPEC §2.2's <c>degradation.phase</c> table.</summary>
    public static class DegradationPhases
    {
        public const string LeaveTypeLookup = "leave_type_lookup";
        public const string ConflictCheck = "conflict_check";
        public const string EmployeeLookup = "employee_lookup";
        public const string Submission = "submission";
    }

    /// <summary>The kinds of SPEC §2.2's <c>degradation.kind</c> table.</summary>
    public static class DegradationKinds
    {
        public const string Timeout = "timeout";
        public const string Error = "error";
        public const string Empty = "empty";
        public const string Malformed = "malformed";
    }

    /// <summary>The rows of SPEC §6, so a refusal says which rule it rests on.</summary>
    public static class RefusalRules
    {
        public const string ApprovalIsAManagerAction = "O-1";
        public const string CannotModifyBookings = "O-2";
        public const string OnlyForTheSignedInUser = "O-3";
        public const string PayrollBelongsToHr = "O-5";
        public const string NoMedicalJudgement = "O-6";
        public const string MissingCapability = "O-7";
    }

    /// <summary>Values for <see cref="Attributes.ConfirmationConflictCheck"/>.</summary>
    public static class ConflictCheckStates
    {
        public const string Clean = "clean";
        public const string ConflictsFound = "conflicts_found";
        public const string NotRun = "not_run";
    }

    /// <summary>The closed set from SPEC §2.3, in precedence order, highest first.</summary>
    public static class TurnOutcomes
    {
        public const string Refused = "refused";
        public const string Degraded = "degraded";
        public const string ClarificationRequested = "clarification_requested";
        public const string ConfirmationPending = "confirmation_pending";
        public const string Cancelled = "cancelled";
        public const string Completed = "completed";
    }

    public static class TerminationReasons
    {
        public const string Decision = "decision";
        public const string IterationCap = "iteration_cap";
        public const string Error = "error";
    }
}
