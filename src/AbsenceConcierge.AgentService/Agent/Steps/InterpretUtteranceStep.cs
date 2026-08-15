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

        context.Intent = await interpreter
            .InterpretAsync(context.Request.Content, cancellationToken)
            .ConfigureAwait(false);

        if (context.Intent.Kind == IntentKind.Unclear)
        {
            context.AskFor(AgentDiagnostics.ClarificationReasons.NothingRequested);
            return StepSignal.Stop;
        }

        return StepSignal.Continue;
    }
}
