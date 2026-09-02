using AbsenceConcierge.AgentService.Telemetry;
using AbsenceConcierge.AgentService.Workforce;
using AbsenceConcierge.Evals.Execution;
using AbsenceConcierge.Evals.Scenarios;

namespace AbsenceConcierge.Evals.Extraction;

/// <summary>
/// Everything the extractor cannot learn from a trace: what to call the scenario,
/// which class it belongs to, and the world it ran in.
/// </summary>
/// <param name="Id">Filename and id. The corpus rules require the two to match.</param>
/// <param name="Class">happy · ambiguity · denied · adversarial · degradation. A human's judgement.</param>
/// <param name="Fixture">The world, the clock and the timezone the turn ran against.</param>
/// <param name="Conversation">What was said, in order.</param>
/// <param name="Reference">A trace id or an incident number. Never a name, never an email.</param>
/// <param name="Date">The day it happened, as yyyy-MM-dd.</param>
public sealed record ExtractionRequest(
    string Id,
    string Class,
    ScenarioFixture Fixture,
    IReadOnlyList<ScenarioTurn> Conversation,
    string? Reference,
    string Date);

/// <summary>
/// Turns a recorded trace into a scenario.
///
/// <para>
/// <b>The claim this is here to make good on.</b> AI-EVALS.md §3 requires every
/// production incident to become a scenario before it becomes a fix, and the
/// scenario schema has carried <c>origin.kind: production-trace</c> since Phase 1.
/// Until now that was a promise about a habit: somebody would read a trace, decide
/// what mattered in it, and hand-write assertions. That reconstruction is where
/// incident scenarios go wrong — the assertions end up describing what the author
/// remembers rather than what happened, and the ones nobody thought to write are
/// exactly the ones the incident turned on.
/// </para>
/// <para>
/// So the trace is not read by a person. Every assertion below is derived
/// mechanically from what the trace contains, including the ones a person would not
/// have written: <b>one <c>tool_not_called</c> for every tool that was not called.</b>
/// That is the assertion discipline this repository enforces on hand-written
/// scenarios (E2E-ACCEPTANCE-TESTING.md §2), applied where it is hardest to remember.
/// </para>
/// <para>
/// <b>What extraction cannot do is decide the behaviour was correct.</b> An
/// extracted scenario locks in what the agent did, which is only worth having once a
/// human has said it should have done that. The <c>why</c> is therefore emitted as a
/// REVIEW marker, and <c>scripts/validate-scenarios.mjs</c> refuses to let a scenario
/// carrying one into the corpus. Extraction gets the trace onto the page; a human
/// still says what it means.
/// </para>
/// </summary>
public static class ScenarioExtractor
{
    /// <summary>
    /// The prefix a human must delete. Committed as a constant because two places
    /// depend on the exact string — this file writes it, the corpus validator
    /// rejects it — and a literal in both is a rule that stops being enforced the day
    /// one of them is reworded.
    /// </summary>
    public const string ReviewMarker = "REVIEW:";

    /// <summary>
    /// Names the turns that did not end by decision, when any did not.
    ///
    /// <para>
    /// The extracted scenario asserts <c>decision</c> whatever the trace showed, so
    /// on a trace like this it fails against the very session it came from. That is
    /// the correct outcome — the session contains a real defect and the scenario is
    /// the regression test for it — but a reviewer who cannot tell that apart from a
    /// harness bug will spend the afternoon finding out. So the file says which
    /// turns, and what they did, in the note a human is already required to read.
    /// </para>
    /// </summary>
    private static string TerminationNote(TraceRecording trace)
    {
        var wrong = trace.Turns
            .Where(turn => !string.Equals(
                turn.TerminationReason,
                AgentDiagnostics.TerminationReasons.Decision,
                StringComparison.Ordinal))
            .Select(turn => $"turn {turn.Index} ended as '{turn.TerminationReason}'")
            .ToList();

        return wrong.Count == 0
            ? string.Empty
            : $" NOTE: this scenario asserts termination by decision, as every scenario must (C-4), "
              + $"but the trace it came from did not: {string.Join("; ", wrong)}. It will therefore fail "
              + "against its own source until the agent is fixed, which is the point of it.";
    }

    public static ScenarioFile From(ScenarioRun run, ExtractionRequest request)
    {
        ArgumentNullException.ThrowIfNull(run);
        ArgumentNullException.ThrowIfNull(request);

        if (run.Trace.Turns.Count == 0)
        {
            // A trace with no turns produces a scenario whose every assertion is
            // vacuously satisfiable. That is the failure this repository is about, so
            // it is an error rather than an empty file.
            throw new InvalidOperationException(
                $"The trace for '{request.Id}' has no turns. A scenario extracted from it would assert "
                + "nothing and pass everything.");
        }

        return new ScenarioFile
        {
            Id = request.Id,
            Class = request.Class,

            // Behaviour, never constraint. A constraint scenario hard-blocks the
            // build, and promoting a freshly extracted trace to that status is a
            // decision about what must never regress — which is a human's to make.
            Gate = "behaviour",
            Title = $"{ReviewMarker} extracted from a trace on {request.Date}",
            Why = $"{ReviewMarker} this scenario records what the agent did, not yet what it should do. "
                + "Replace this with why the behaviour below is correct — or change the assertions and "
                + "fix the agent. A scenario that only says 'this is what happened' locks in the bug."
                + TerminationNote(run.Trace),

            Origin = new ScenarioOrigin
            {
                Kind = "production-trace",
                Reference = request.Reference,
                Date = request.Date,
            },

            Fixture = request.Fixture,
            Conversation = [.. request.Conversation],
            Expect = [.. Assertions(run)],
            Rubrics = [],
        };
    }

    private static IEnumerable<ScenarioAssertion> Assertions(ScenarioRun run)
    {
        var trace = run.Trace;

        // ── What was called, exactly as many times ──────────────────────────────
        //
        // `times`, not `at_least`. An extracted scenario's job is to pin the trace it
        // came from; `at_least: 1` would let the agent that submits twice against one
        // confirmation pass a scenario extracted from the run where it submitted once,
        // which is precisely the hole the mutation pass found in deg-003 and deg-004.

        var calls = trace.ToolCalls
            .GroupBy(call => call.Tool, StringComparer.Ordinal)
            .OrderBy(group => group.Key, StringComparer.Ordinal)
            .ToList();

        foreach (var group in calls)
        {
            yield return new ScenarioAssertion
            {
                Assert = "tool_called",
                Tool = group.Key,
                Times = group.Count(),
            };
        }

        // ── What was not called ────────────────────────────────────────────────
        //
        // The half of a scenario that gets forgotten. A run in which the agent
        // refused is only evidence of a refusal if the tool it refused to use is
        // asserted absent — an agent that refuses politely and calls the tool anyway
        // passes every assertion above.

        var called = calls.Select(group => group.Key).ToHashSet(StringComparer.Ordinal);

        foreach (var tool in WorkforceToolCatalog.Names.Order(StringComparer.Ordinal))
        {
            if (!called.Contains(tool))
            {
                yield return new ScenarioAssertion { Assert = "tool_not_called", Tool = tool };
            }
        }

        // ── The write's arguments, and where they came from ─────────────────────

        var write = trace.ToolCalls.LastOrDefault(call =>
            string.Equals(call.Kind, "write", StringComparison.Ordinal));

        if (write is not null && write.Arguments.Count > 0)
        {
            yield return new ScenarioAssertion
            {
                Assert = "tool_called_with",
                Tool = write.Tool,

                // Subset: the recorded arguments are what the span carries, and the
                // span deliberately omits the confirmation token. `exact` would assert
                // the absence of a field that was never recorded in the first place.
                Match = "subset",
                Args = write.Arguments.ToDictionary(pair => pair.Key, pair => pair.Value, StringComparer.Ordinal),
            };

            foreach (var grounded in Grounded(trace, write))
            {
                yield return grounded;
            }
        }

        // ── Contract events ────────────────────────────────────────────────────

        foreach (var group in trace.Events
            .GroupBy(emitted => emitted.Name, StringComparer.Ordinal)
            .OrderBy(group => group.Key, StringComparer.Ordinal))
        {
            yield return new ScenarioAssertion
            {
                Assert = "event_emitted",
                Event = group.Key,
                Times = group.Count(),
            };
        }

        // ── The gate, as an ordering ───────────────────────────────────────────
        //
        // Derived rather than assumed: emitted only when the trace actually contains
        // both halves. An extractor that emitted this unconditionally would produce a
        // scenario that fails on every refusal path for the wrong reason.

        if (write is not null && trace.Events.Any(emitted =>
            string.Equals(emitted.Name, AgentDiagnostics.Events.ConfirmationReceived, StringComparison.Ordinal)))
        {
            yield return new ScenarioAssertion
            {
                Assert = "order",
                First = new SpanReference { Event = AgentDiagnostics.Events.ConfirmationReceived },
                Then = new SpanReference { Tool = write.Tool },
            };
        }

        // ── Retry bounds ───────────────────────────────────────────────────────
        //
        // Only where a call actually retried. Pinning `max_attempts: 1` on every
        // quiet tool would turn the first legitimate retry into a corpus-wide
        // failure, which teaches people to edit scenarios rather than read them.

        foreach (var group in calls.Where(group => group.Max(call => call.Attempts) > 1))
        {
            yield return new ScenarioAssertion
            {
                Assert = "call_attempts",
                Tool = group.Key,
                MaxAttempts = group.Max(call => call.Attempts),
            };
        }

        // ── How the turn ended ─────────────────────────────────────────────────

        var last = trace.Turns[^1];

        yield return new ScenarioAssertion { Assert = "outcome", Turn = "last", Value = last.Outcome };

        // Always `decision`, never the reason the trace happened to show.
        //
        // C-4 is not a property recovered from a trace; it is the requirement the
        // trace is measured against. Every scenario in the corpus asserts
        // `decision`, and validate-scenarios.mjs rejects a scenario without the
        // check for exactly that reason: it must "prove the loop ended by decision
        // rather than by hitting the iteration cap".
        //
        // This extractor's input is the worst-sessions upload — sessions that went
        // wrong. Pinning the observed reason meant that a session whose last turn
        // exhausted the cap produced a scenario REQUIRING the agent to exhaust the
        // cap, and failing it for terminating properly. That inverts the constraint
        // for every future run, wearing a production-trace origin, which is the
        // provenance a reader trusts most.
        //
        // It also contradicted this file's own Why text three hundred lines up: a
        // scenario that only says "this is what happened" locks in the bug.
        yield return new ScenarioAssertion
        {
            Assert = "termination",
            Reason = AgentDiagnostics.TerminationReasons.Decision,
        };

        // Universal, and cheap to check: an identifier in a reply is a leak whatever
        // the scenario is about (SPEC O-7).
        yield return new ScenarioAssertion { Assert = "output_excludes_internal_ids" };
    }

    /// <summary>
    /// One <c>argument_grounded</c> per write argument whose value was returned by an
    /// earlier tool call.
    ///
    /// <para>
    /// This is C-5 recovered from the trace rather than asserted from memory. It is
    /// also the assertion most likely to be missing from a hand-written incident
    /// scenario, because "the id came from somewhere real" is the thing everybody
    /// assumes and nobody checks — right up until a model supplies one from its
    /// training data and the request books against a leave type that does not exist.
    /// </para>
    /// </summary>
    private static IEnumerable<ScenarioAssertion> Grounded(TraceRecording trace, ToolCallRecord write)
    {
        foreach (var argument in write.Arguments.OrderBy(pair => pair.Key, StringComparer.Ordinal))
        {
            var source = trace.ToolCalls
                .Where(call => call.Position < write.Position)
                .FirstOrDefault(call => call.ResultIds.Contains(argument.Value, StringComparer.Ordinal));

            if (source is not null)
            {
                yield return new ScenarioAssertion
                {
                    Assert = "argument_grounded",
                    Tool = write.Tool,
                    Arg = argument.Key,
                    SourceTool = source.Tool,
                };
            }
        }
    }
}
