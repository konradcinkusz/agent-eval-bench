using AbsenceConcierge.AgentService.Agent.Language;
using AbsenceConcierge.AgentService.Telemetry;

namespace AbsenceConcierge.AgentService.Agent.Steps;

/// <summary>
/// Turns what the user said into a typed <see cref="Intent"/>, and reports any
/// instruction-shaped content the sentence carried.
///
/// <para>
/// The user's own words get the same treatment as a tool result: scanned, reported,
/// and then used only through the typed fields the interpreter produced. A "SYSTEM
/// NOTE" in a message is a string in a message.
/// </para>
/// </summary>
public sealed class InterpretUtteranceStep(IUtteranceInterpreter interpreter) : IAgentStep
{
    public string Name => "interpret_utterance";

    public bool AppliesTo(AgentTurnContext context) => context?.Request.Decision is null;

    public async ValueTask<StepSignal> ExecuteAsync(
        AgentTurnContext context,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(context);

        foreach (var signal in InstructionShapedContent.Scan(context.Request.Content))
        {
            context.NoteIgnoredInstruction(new InstructionShapedFinding(
                "user_input",
                Tool: null,
                "content",
                signal));
        }

        var read = await interpreter
            .InterpretAsync(context.Request.Content, cancellationToken)
            .ConfigureAwait(false);

        // B-17. The held intent goes UNDERNEATH: a field this turn supplies wins,
        // and only the gaps are filled. "Book the 3rd off instead" after a
        // clarification about sick leave keeps its own date and its own type; "the
        // 21st" keeps the hint it is answering about.
        //
        // Taking it clears it, so the hold lasts exactly one turn without anything
        // having to remember to reset it.
        context.Intent = Merge(read, context.Conversation.TakeIntent());

        if (context.Intent.Kind == IntentKind.Unclear)
        {
            context.AskFor(AgentDiagnostics.ClarificationReasons.NothingRequested);
            return StepSignal.Stop;
        }

        return StepSignal.Continue;
    }

    /// <summary>
    /// This turn's reading, with the gaps filled from what an earlier turn
    /// established. Kind is taken from the held intent only when this turn has no
    /// reading of its own — an answer like "the 21st" classifies as nothing on its
    /// own, and the request it completes is the one that was already in flight.
    /// </summary>
    private static Intent Merge(Intent read, Intent? held)
    {
        if (held is null)
        {
            return read;
        }

        return read with
        {
            Kind = read.Kind == IntentKind.Unclear ? held.Kind : read.Kind,
            Dates = read.Dates ?? held.Dates,
            LeaveTypeHint = read.LeaveTypeHint ?? held.LeaveTypeHint,
            Person = read.Person ?? held.Person,
            ClaimsPriorApproval = read.ClaimsPriorApproval || held.ClaimsPriorApproval,
        };
    }
}
