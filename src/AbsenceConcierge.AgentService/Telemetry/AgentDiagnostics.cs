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

        /// <summary>read | write — from the catalogue, never inferred from the name.</summary>
        public const string ToolKind = "workforce.tool.kind";
        public const string ToolOutcome = "workforce.tool.outcome";
        public const string ToolArguments = "workforce.tool.arguments";

        public const string TurnOutcome = "agent.turn.outcome";
        public const string TerminationReason = "agent.termination.reason";

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
