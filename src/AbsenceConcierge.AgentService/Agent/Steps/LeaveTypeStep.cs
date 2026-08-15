using AbsenceConcierge.AgentService.Agent.Language;
using AbsenceConcierge.AgentService.Telemetry;
using AbsenceConcierge.AgentService.Workforce;
using Microsoft.Extensions.Options;

namespace AbsenceConcierge.AgentService.Agent.Steps;

/// <summary>
/// Retrieves the leave types the actor may use, and maps the user's word to one of
/// them.
///
/// <para>
/// <b>Retrieval before naming (B-2), always.</b> The agent has no built-in idea of
/// what leave types exist and no memory of what they are usually called. That is
/// what makes C-5 checkable: the identifier in a write must have appeared in a tool
/// result in the same trace, and here is where it appears.
/// </para>
/// <para>
/// <b>"No word given" and "no type matches" are different answers (B-3).</b> A user
/// who said "book Friday off" gets the company's default leave type; a user who
/// said "there's a funeral in the family" gets a question, because no retrieved type
/// covers it and picking the closest one on their behalf is precisely the confident
/// guess this agent exists not to make.
/// </para>
/// </summary>
public sealed class LeaveTypeStep(IWorkforceTools tools, IOptions<AgentOptions> options) : IAgentStep
{
    private readonly AgentOptions _options = options.Value;

    public string Name => "retrieve_leave_types";

    public bool AppliesTo(AgentTurnContext context) => context?.Intent is { Kind: IntentKind.RequestTimeOff };

    public async ValueTask<StepSignal> ExecuteAsync(
        AgentTurnContext context,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(context);

        var result = await tools.ListLeaveTypesAsync(cancellationToken).ConfigureAwait(false);

        if (!result.IsSuccess || result.Value is null)
        {
            context.NoteDegradation(
                AgentDiagnostics.DegradationPhases.LeaveTypeLookup,
                WorkforceToolCatalog.ListLeaveTypes,
                result.Outcome == ToolOutcome.Indeterminate
                    ? AgentDiagnostics.DegradationKinds.Timeout
                    : AgentDiagnostics.DegradationKinds.Error);

            // §7 rule 2: a missing leave-type list does not become a remembered one.
            // There is no draft to offer, so the turn ends here saying so.
            return StepSignal.Stop;
        }

        if (result.Value.Count == 0)
        {
            // An empty list is a successful call that answered nothing. Treating it
            // as success would produce "no leave types matched your request", which
            // blames the user for a backend that returned nothing.
            context.NoteDegradation(
                AgentDiagnostics.DegradationPhases.LeaveTypeLookup,
                WorkforceToolCatalog.ListLeaveTypes,
                AgentDiagnostics.DegradationKinds.Empty);

            return StepSignal.Stop;
        }

        context.LeaveTypes = result.Value;
        context.Conversation.RecordRetrievedLeaveTypes(result.Value);

        foreach (var leaveType in result.Value)
        {
            foreach (var signal in InstructionShapedContent.Scan(leaveType.Name))
            {
                context.NoteIgnoredInstruction(new InstructionShapedFinding(
                    "tool_result",
                    WorkforceToolCatalog.ListLeaveTypes,
                    "name",
                    signal));
            }
        }

        var selected = Select(context.Intent!.LeaveTypeHint, result.Value);

        if (selected is null)
        {
            context.AskFor(AgentDiagnostics.ClarificationReasons.NoMatchingLeaveType);
            return StepSignal.Stop;
        }

        context.SelectedLeaveType = selected;
        return StepSignal.Continue;
    }

    private LeaveType? Select(string? hint, IReadOnlyList<LeaveType> available)
    {
        if (hint is not null)
        {
            // Matched against the retrieved names, in both directions, so "sick"
            // finds "Sick leave" and "annual leave" finds "Leave". Nothing is matched
            // against a remembered catalogue.
            return available.FirstOrDefault(type =>
                type.Name.Contains(hint, StringComparison.OrdinalIgnoreCase)
                || hint.Contains(type.Name, StringComparison.OrdinalIgnoreCase));
        }

        foreach (var preference in _options.DefaultLeaveTypePreference)
        {
            var match = available.FirstOrDefault(type =>
                type.Name.Contains(preference, StringComparison.OrdinalIgnoreCase));

            if (match is not null)
            {
                return match;
            }
        }

        // One type in the whole catalogue is not a choice. More than one, with
        // nothing in the sentence and nothing in the preference list to separate
        // them, is a question.
        return available.Count == 1 ? available[0] : null;
    }
}
