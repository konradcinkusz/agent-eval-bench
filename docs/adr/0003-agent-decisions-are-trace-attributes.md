# ADR-0003: The agent's decision is a trace attribute, not prose

- **Status**: Accepted
- **Date**: 2026-08-14
- **Phase**: 1 — spec and scenarios
- **Relates to**: AI-EVALS.md §4, P15, E2E-ACCEPTANCE-TESTING.md §2

## Context

Layer 1 grades scenarios by asserting over the execution trace. That only works
if the things it needs to assert are *in* the trace as structure. The awkward
cases are the agent's own decisions:

- Did it refuse, or did it comply while sounding hesitant?
- Did it ask a clarifying question, or did it guess and phrase the guess as a
  question ("I'll book Friday the 21st, sound good?")?
- Did it stop for confirmation, or did it write and then describe the write in
  the past tense?

Each of those distinctions is the difference between a passing and a failing
scenario, and each is invisible in the reply text without interpretation. The
obvious implementation is to match on the text: look for "I can't", look for a
question mark, look for "I've booked".

That approach fails in both directions, and the estate has already paid for the
lesson in a different form. The E2E audit found helper functions whose names
promised an assertion they did not make — thirteen call sites that could never
fail, discovered only by reading the helper rather than trusting its signature. A
text matcher is the same shape of trap: `reply.Contains("I can't")` looks like it
asserts a refusal, and asserts a substring.

The two failure directions, concretely:

- **False regressions.** Rewording a prompt from "I can't do that" to "That's
  outside what I can help with" breaks every refusal scenario, with no behaviour
  change whatsoever. A suite that goes red on a synonym is a suite that gets
  re-baselined without being read, which is how a real regression eventually
  ships inside a batch of "just wording" failures.
- **False passes.** An agent that writes the booking and *then* says "shall I go
  ahead?" passes a question-mark check while having violated the one constraint
  this entire repository is built around.

## Decision

Every turn carries an `agent.turn.outcome` span attribute with exactly one value
from a closed set:

`completed` · `refused` · `clarification_requested` · `confirmation_pending` ·
`cancelled` · `degraded`

The orchestrator sets it as a **structural consequence of the path taken through
the step pipeline** — the refusal step sets `refused`, the confirmation gate sets
`confirmation_pending`, the executor sets `completed`. It is never derived from
the model's output, and the model is never asked to declare it.

Alongside it, the discrete decisions are trace **events** with the same property:
`confirmation.shown`, `confirmation.received`, `refusal.issued`,
`clarification.requested`, `degradation.noted`, `injection.ignored`.

Layer 1 asserts on these and on tool spans. **Layer 1 never matches reply text.**
Grading the wording is Layer 2's job, against rubric anchors, where a synonym is
correctly a non-event.

The one apparent exception is `output_excludes_internal_ids` ([C-3](../SPEC.md#4-hard-constraints)),
which does search user-facing output. It searches for `^(emp|lt|lv|req)-[0-9]{3,4}$`
— a pattern the fixtures guarantee by construction. That is a decidable property
of a generated string, not an interpretation of prose, and it is the only text
assertion in Layer 1.

## Alternatives considered

### Match the reply text with regexes or keyword lists

**Why it is attractive:** zero instrumentation work; scenarios read like a user
would read them; it is what most eval harnesses do.

**Why it lost:** both failure directions above, and the second one is
disqualifying on its own. A harness that can pass an agent which wrote before
asking cannot be used to gate the constraint that a write requires a
confirmation. It also couples every scenario to prompt wording, which is the one
thing this suite exists to let people change safely.

### Ask the model to emit a structured decision (a JSON field, a tool call named `refuse`)

**Why it is attractive:** structure without orchestrator plumbing, and it works
with a thin agent loop where the model drives everything.

**Why it lost:** the value would then be *the model's claim about what it did*,
graded as if it were a record of what happened. Under adversarial input — which
is an entire scenario class here — a model that has been talked into writing has
also been talked into labelling the turn `confirmation_pending`. The attribute
must be set by the code that took the action, not by the thing being tested.
Self-reported compliance is not compliance.

### Have Layer 2's judge determine the outcome and feed it back to Layer 1

**Why it is attractive:** the judge already reads the trace, and it handles
nuance a closed enum cannot.

**Why it lost:** it makes the constraint gate depend on a model call — cost on
every pull request, non-determinism in the one layer that must be deterministic,
and an API key required for the gate that is supposed to run on a fresh clone
with none. Constraints are the cheap, fast, model-independent layer; putting a
model inside them defeats the split.

## Consequences

**What this makes easy:** prompts can be rewritten freely without touching a
scenario; constraint assertions are exact rather than probabilistic; the same
attribute that gates a merge offline is the field production scoring aggregates
online, so a low-scoring live session converts to a scenario by extraction rather
than authorship (AI-EVALS.md §7).

**What this makes hard:** the orchestrator must be structured enough that "which
path did we take" is knowable — the outcome cannot be set honestly by a single
free-running loop that does everything in one step. That pushes the design toward
the chain-of-responsibility step pipeline the estate already uses, which is a
constraint on the implementation, imposed here by an evaluation requirement.

**What we accept:** the enum is closed, so a genuinely new kind of turn outcome
requires a spec change, a schema change and a re-baseline. That friction is
intentional — an open set would drift into a free-text field within two phases,
and a free-text outcome field is prose with extra steps.

## Revisit when

A turn legitimately has two outcomes at once — for example a partially degraded
run that still reaches a confirmation. Version 1.0.0 forces a single value and
ranks `degraded` above `confirmation_pending` when both apply, which loses
information. If more than one scenario needs the pair, the attribute should
become an outcome plus a set of modifiers rather than one enum.
