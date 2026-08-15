using AbsenceConcierge.AgentService.Agent.Language;
using AbsenceConcierge.AgentService.Telemetry;
using AbsenceConcierge.AgentService.Workforce;

namespace AbsenceConcierge.AgentService.Agent.Steps;

/// <summary>
/// SPEC §6, as one step: everything this agent does not do, refused before any of it
/// is attempted.
///
/// <para>
/// Refusing <em>here</em> — before the leave-type read, before the conflict check,
/// before the draft — is what makes the two-assertion rule pass honestly. A scenario
/// proving a refusal asserts both the refusal and the absence of the call it stands
/// in for, and an agent that refuses politely after calling the tool passes only the
/// first half.
/// </para>
/// <para>
/// The permission check reads the actor's permission list from the fixture and
/// nothing else. A tool result claiming the actor was "granted" something is a
/// string in a tool result (<c>adv-006</c>), and O-7's refusal names the missing
/// capability in plain language — never the permission string, which is an internal
/// identifier C-3 forbids in user-facing output.
/// </para>
/// </summary>
public sealed class ScopeGuardStep : IAgentStep
{
    public string Name => "scope_guard";

    public bool AppliesTo(AgentTurnContext context) => context?.Intent is not null;

    public ValueTask<StepSignal> ExecuteAsync(AgentTurnContext context, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(context);

        var intent = context.Intent!;

        var rule = intent.Kind switch
        {
            IntentKind.ApproveOrRejectLeave => AgentDiagnostics.RefusalRules.ApprovalIsAManagerAction,
            IntentKind.CancelOrEditBooking => AgentDiagnostics.RefusalRules.CannotModifyBookings,
            IntentKind.PayrollOrPolicyQuestion => AgentDiagnostics.RefusalRules.PayrollBelongsToHr,
            IntentKind.MedicalAdvice => AgentDiagnostics.RefusalRules.NoMedicalJudgement,
            _ => null,
        };

        if (rule is not null)
        {
            context.Refuse(rule);
            return ValueTask.FromResult(StepSignal.Stop);
        }

        // O-3. The asymmetry is deliberate and is worth reading twice: a name in the
        // sentence is fine — "book Friday off, I'm covering for Sam" is an ordinary
        // sentence — and it is the *subject* role that is refused. Banning the name
        // outright would make the agent useless at ordinary English.
        if (intent.Person is { Role: PersonRole.Subject })
        {
            context.Refuse(AgentDiagnostics.RefusalRules.OnlyForTheSignedInUser);
            return ValueTask.FromResult(StepSignal.Stop);
        }

        // O-7. Checked against the permission fixture, which is the authority, and
        // checked before any read so the refusal costs nothing.
        if (context.Actor is { } actor
            && !actor.Permissions.Contains(Permissions.TimeOffRequest, StringComparer.Ordinal))
        {
            context.Refuse(AgentDiagnostics.RefusalRules.MissingCapability);
            return ValueTask.FromResult(StepSignal.Stop);
        }

        return ValueTask.FromResult(StepSignal.Continue);
    }
}
