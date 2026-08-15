using AbsenceConcierge.AgentService.Agent.Language;
using AbsenceConcierge.AgentService.Telemetry;
using AbsenceConcierge.AgentService.Workforce;

namespace AbsenceConcierge.AgentService.Agent.Steps;

/// <summary>
/// Looks up a person the sentence named.
///
/// <para>
/// Reaching the directory is allowed and refusing to write for someone else is
/// mandatory; the subject case was already refused by <see cref="ScopeGuardStep"/>,
/// so what arrives here is a name used as a date reference ("the same week off as
/// Sam") or mentioned in passing ("Dana is covering for me").
/// </para>
/// <para>
/// A name matching two colleagues is a question, not a coin toss (B-13). The base
/// fixture contains two people called Sam Rivera for exactly this reason: name
/// collision is the most common real ambiguity in an HR tool, and the two are
/// distinguished by team in the question the agent asks.
/// </para>
/// </summary>
public sealed class ResolvePersonStep(IWorkforceTools tools) : IAgentStep
{
    public string Name => "resolve_person";

    public bool AppliesTo(AgentTurnContext context) =>
        context?.Intent is { Kind: IntentKind.RequestTimeOff, Person: not null };

    public async ValueTask<StepSignal> ExecuteAsync(
        AgentTurnContext context,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(context);

        var person = context.Intent!.Person!;

        // C-2: a tool whose permission the actor does not hold is not called at all.
        // The name simply stays unresolved, which for a passing mention costs nothing.
        if (context.Actor is { } actor
            && !actor.Permissions.Contains(Permissions.DirectoryRead, StringComparer.Ordinal))
        {
            return person.Role == PersonRole.DateReference
                ? Ask(context, AgentDiagnostics.ClarificationReasons.DatesFromAnotherPerson)
                : StepSignal.Continue;
        }

        var result = await tools.FindEmployeeAsync(person.Name, cancellationToken).ConfigureAwait(false);

        if (!result.IsSuccess || result.Value is null)
        {
            context.NoteDegradation(
                AgentDiagnostics.DegradationPhases.EmployeeLookup,
                WorkforceToolCatalog.FindEmployee,
                AgentDiagnostics.DegradationKinds.Error);

            return person.Role == PersonRole.DateReference ? StepSignal.Stop : StepSignal.Continue;
        }

        context.EmployeeMatches = result.Value;

        foreach (var employee in result.Value)
        {
            foreach (var signal in InstructionShapedContent.Scan(employee.DisplayName))
            {
                context.NoteIgnoredInstruction(new InstructionShapedFinding(
                    "tool_result",
                    WorkforceToolCatalog.FindEmployee,
                    "display_name",
                    signal));
            }
        }

        if (person.Role != PersonRole.DateReference)
        {
            return StepSignal.Continue;
        }

        // Even a single match does not settle it: another employee's bookings are
        // not the actor's to read (`only_for_self` in the fixture's tool policy), so
        // "the same week as Sam" cannot be resolved from data this agent may see.
        // Asking is the answer, and it is the honest one.
        return result.Value.Count == 1
            ? Ask(context, AgentDiagnostics.ClarificationReasons.DatesFromAnotherPerson)
            : Ask(context, AgentDiagnostics.ClarificationReasons.AmbiguousEmployee);
    }

    private static StepSignal Ask(AgentTurnContext context, string reason)
    {
        context.AskFor(reason);
        return StepSignal.Stop;
    }
}
