using System.Globalization;
using AbsenceConcierge.AgentService.Agent.Llm;
using AbsenceConcierge.AgentService.Demo;
using Microsoft.Extensions.Options;

namespace AbsenceConcierge.AgentService.Agent.Language;

/// <summary>
/// The live composer: a model rewrites the deterministic reply, and can do nothing
/// else.
///
/// <para>
/// <b>Where the model sits is the whole design.</b> By the time a composer runs the
/// steps have decided, the gate has held or not, the write has happened or not, and
/// the outcome is already a trace attribute (ADR-0003). A model here cannot call a
/// tool, cannot skip a confirmation, cannot change a date and cannot change an
/// outcome — the worst it can do is phrase something badly, and the checks below
/// bound even that. That is the thesis of this repository in one class: <b>the model
/// writes, the pipeline decides.</b>
/// </para>
/// <para>
/// <b>The user's words never reach the model.</b> Its entire input is the reply the
/// deterministic composer already produced, and its entire instruction is to say the
/// same thing better. So the injection surface of this feature is not the user's
/// message at all — it is at most a hostile string that a tool returned and the
/// deterministic composer already chose to include, and the output of obeying one is
/// prose, in a reply whose facts are checked against the turn before it is used.
/// </para>
/// <para>
/// <b>Every failure is a fallback, never an error.</b> No credential, no budget, a
/// timeout, an empty answer, an answer that grew, an answer containing an identifier
/// this turn was supposed to keep out of sight — each one returns the deterministic
/// reply. A visitor never sees the feature fail; they see the page's banner say
/// which composer answered, which is [ADR-0004]'s "never fall back silently" applied
/// to the one surface a stranger looks at.
/// </para>
/// </summary>
public sealed class ModelBackedReplyComposer(
    IReplyComposer grounded,
    ILlmProvider provider,
    IDemoBudget budget,
    IPromptLibrary prompts,
    IOptions<DemoOptions> options,
    ILogger<ModelBackedReplyComposer> logger) : IReplyComposer
{
    public const string ComposerName = "model";

    /// <summary>
    /// How much longer than the deterministic reply the model's version may be.
    ///
    /// <para>
    /// A ceiling rather than a token count, because the failure it guards against is
    /// a model that starts explaining itself, apologising, or inventing a policy —
    /// all of which show up as length long before they show up as anything else. The
    /// deterministic reply is the right length by construction; roughly twice it is
    /// generous, and past that the rewrite has stopped being a rewrite.
    /// </para>
    /// </summary>
    private const int LengthAllowance = 200;

    private readonly DemoOptions _options = options.Value;

    public async ValueTask<string> ComposeAsync(
        AgentTurnContext context,
        string outcome,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(context);

        // The grounded reply is produced on every path, including the ones that
        // never call the model. It is both the fallback and the model's input, so a
        // composite that skipped it when live would have nothing to fall back to at
        // the moment it most needs one.
        var reply = await grounded.ComposeAsync(context, outcome, cancellationToken).ConfigureAwait(false);

        if (!context.Request.UseModel)
        {
            return reply;
        }

        var ceiling = Math.Max(1, _options.MaxOutputTokensPerReply);

        if (!budget.TryReserve(ceiling))
        {
            logger.LogInformation("The live composer was asked for but today's token budget is spent.");
            return reply;
        }

        var spent = 0;

        try
        {
            var response = await provider.CompleteAsync(
                new LlmRequest(
                    prompts.Read(PromptLibrary.ReplyComposer),
                    [new LlmMessage("user", Brief(context, outcome, reply))],
                    ceiling),
                cancellationToken).ConfigureAwait(false);

            spent = response.OutputTokens;

            if (Rejected(response, reply, context) is { } reason)
            {
                logger.LogWarning(
                    "The model's reply was not used: {Reason}. The deterministic reply was sent instead.",
                    reason);

                return reply;
            }

            return response.Text.Trim();
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            logger.LogWarning("The live composer timed out. The deterministic reply was sent instead.");
            return reply;
        }
        catch (OperationCanceledException)
        {
            // The caller went away. That is not a degradation to paper over with a
            // reply nobody is waiting for — it is the one failure here that should
            // propagate, and it must be caught before the general handler below.
            throw;
        }
#pragma warning disable CA1031 // Any provider failure degrades; none of them may fail a turn.
        catch (Exception exception)
        {
            // A turn that already decided correctly must not fail because the prose
            // was going to be nicer. This is P8 at the granularity of one sentence.
            logger.LogWarning(exception, "The live composer failed. The deterministic reply was sent instead.");
            return reply;
        }
#pragma warning restore CA1031
        finally
        {
            budget.Settle(ceiling, spent);
        }
    }

    /// <summary>
    /// Why the model's reply cannot be used, or <c>null</c> when it can.
    ///
    /// <para>
    /// The identifier check is exact rather than heuristic: it looks for the actual
    /// identifiers this turn handled — the actor's employee id, every leave type id
    /// retrieved, every existing booking's id, and the request id if one was
    /// created. A regex for "looks like an id" would flag ordinary prose and be
    /// switched off within a month; a list of the strings that must not appear
    /// cannot be wrong about what it is looking for (SPEC §2.4 takes the same line
    /// for the eval assertion).
    /// </para>
    /// </summary>
    private static string? Rejected(LlmResponse response, string grounded, AgentTurnContext context)
    {
        var text = response.Text?.Trim();

        if (string.IsNullOrWhiteSpace(text))
        {
            return "it was empty";
        }

        if (string.Equals(response.FinishReason, "length", StringComparison.OrdinalIgnoreCase))
        {
            // A reply cut off mid-sentence is worse than a plain one, and this is the
            // shape a runaway generation takes at the moment it hits the ceiling.
            return "it hit the output ceiling and was truncated";
        }

        if (text.Length > (grounded.Length * 2) + LengthAllowance)
        {
            return string.Create(
                CultureInfo.InvariantCulture,
                $"it was {text.Length} characters against the grounded reply's {grounded.Length}");
        }

        foreach (var identifier in Identifiers(context))
        {
            if (text.Contains(identifier, StringComparison.OrdinalIgnoreCase))
            {
                // Deliberately not naming the identifier in the log line: the log is
                // the place this repository puts things it will not put in a span or
                // a reply, not a place to repeat one.
                return "it contained an internal identifier (C-3)";
            }
        }

        return null;
    }

    private static IEnumerable<string> Identifiers(AgentTurnContext context)
    {
        if (context.Actor is { } actor)
        {
            yield return actor.EmployeeId;
        }

        foreach (var type in context.LeaveTypes)
        {
            yield return type.Id;
        }

        foreach (var leave in context.ConflictingLeaves)
        {
            yield return leave.Id;
        }

        foreach (var employee in context.EmployeeMatches)
        {
            yield return employee.EmployeeId;
        }

        if (context.WriteResult?.Value is { } written)
        {
            yield return written.RequestId;
        }
    }

    /// <summary>
    /// What the model is given: the outcome, and the sentence to rewrite. Nothing
    /// else, and in particular not the conversation — a rewriter that can see the
    /// user's message is a rewriter that can be told what to write.
    /// </summary>
    private static string Brief(AgentTurnContext context, string outcome, string grounded) =>
        string.Create(
            CultureInfo.InvariantCulture,
            $"""
             OUTCOME: {outcome}
             DEGRADED: {(context.Degradations.Count > 0 ? "yes" : "no")}

             REPLY TO REWRITE:
             {grounded}
             """);
}
