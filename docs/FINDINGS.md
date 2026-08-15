# Findings

What this eval suite has actually caught, in numbers, with the defects named.

A suite that has never failed is a suite nobody has tested. This document is the
answer to "did any of it matter?", and it is deliberately written to be
unflattering: the section that matters most is [§5](#5-what-it-has-not-caught-and-cannot-claim),
which is the list of things the green build says nothing about.

## 1. The suite, in numbers

| | Count |
|---|---|
| Scenarios | **35** (32 English; 3 Spanish since SPEC 1.5.0) |
| Assertions across them | **313** |
| Of those, **absence** assertions (`tool_not_called`, `event_not_emitted`) | **60** (19%) |
| Constraint-gated scenarios (hard-block at 100%) | **20** |
| Behaviour-gated scenarios (measured against a baseline) | **15** |
| Fixture worlds | 1 base + a sparse delta per scenario (plus the e2e world with the hostile leave-type name) |
| Deliberately broken agent variants the suite must catch | **4** |
| Calibration labels for the judge | **45**, from one AI rater — what that is and is not evidence of: [`CALIBRATION.md`](CALIBRATION.md) |

By class:

| Class | Scenarios | Assertions | Mean per scenario |
|---|---|---|---|
| `happy` | 8 | 90 | 11.2 |
| `ambiguity` | 9 | 76 | 8.4 |
| `adversarial` | 7 | 59 | 8.4 |
| `denied` | 6 | 42 | 7.0 |
| `degradation` | 5 | 46 | 9.2 |

By assertion type:

| Type | Count | |
|---|---|---|
| `tool_called` | 48 | |
| `event_emitted` | 48 | |
| `outcome` | 38 | |
| `termination` | 35 | one per scenario — C-4 is not optional |
| `output_excludes_internal_ids` | 35 | one per scenario — C-3 is not optional |
| `event_not_emitted` | 33 | |
| `tool_not_called` | 27 | |
| `order` | 25 | this is where C-1 lives |
| `argument_grounded` | 10 | C-5 |
| `tool_called_with` | 8 | |
| `call_attempts` | 5 | |
| `span_attribute` | 1 | |

**The 19% figure is the one worth looking at.** One assertion in five says
something did *not* happen. That ratio is not aspiration; it is enforced —
[`scripts/validate-scenarios.mjs`](../scripts/validate-scenarios.mjs) fails any
`denied` or `adversarial` scenario without an absence assertion, because
asserting that the agent refused, without asserting that the forbidden call did
not happen, is half a test. An agent that refuses politely and calls the tool
anyway passes the other half.

Current Layer 1 state: **35 of 35 pass**, recorded in
[`evals/baselines/layer1.json`](../evals/baselines/layer1.json). Runtime is
published per run in `TestResults/eval-report.json` and in the sticky pull request
comment; it is not quoted here, because a number copied into prose is a number
that goes stale on the next commit.

## 2. What it caught

Eleven defects. Seven of them were in the measuring instrument or the
specification rather than in the agent, which is itself the finding — see
[§3](#3-where-the-findings-actually-came-from).

| # | Defect | Found by | Phase | Severity |
|---|---|---|---|---|
| F-1 | Two scenarios let a broken agent submit twice against one confirmation | The mutation pass | 4 | **High** — C-6 unenforced |
| F-2 | The specification said the opposite of what the trace does about retries | Chasing F-1 | 4 | **High** — two scenarios written on it |
| F-3 | Two scenarios asserted contradictory answers to the same sentence | Writing the agent | 3 | **High** — no correct agent passes both |
| F-4 | C-5 was specified and unevaluable — nothing recorded what a tool returned | Writing the Layer 1 harness | 4 | Medium |
| F-5 | A unit test repeated F-3's mistake, in the opposite direction | Writing the agent | 3 | Medium |
| F-6 | Three tests broke on a commit that touched none of them | A red build | 3 | Medium |
| F-7 | The agent definition claimed a spec version two releases behind | The definition validator | 7 | Medium |
| F-8 | The analyzer posture had to be relaxed four times on first contact | The first build | 2 | Low |
| F-9 | The grounding anchors have no answer for world-data the trace summarises but does not carry | The calibration labelling pass | 10 | Medium — an instrument ambiguity, found before the judge ever ran |
| F-10 | The composer answers a Spanish speaker in English | The calibration labelling pass | 10 | Medium — every fact right, the register wrong for the fixture's own audience |
| F-11 | The degradation replies say their key sentence twice | The calibration labelling pass | 10 | Low |

### F-9, F-10, F-11 — what labelling caught before the judge ever ran

The calibration protocol's first use produced findings the way running the suite
never had — by making somebody read every transcript against every anchor. The
three are written up where they were found
([`CALIBRATION.md`](CALIBRATION.md), "Who labelled first"), and they split the
usual way: one instrument defect (F-9 — "Monday 12 October (National Day)" is a
claim the trace's `excluded_days=2026-10-12=holiday` supports only halfway, and
the anchor's wording decides neither direction), two agent defects in the one
component Layer 1 deliberately does not grade — the composer (F-10: `hap-007`,
`hap-008` and `amb-009` carry `locale: es-ES` and get English prose back; F-11:
`deg-001` ends "Nothing has been submitted — please try again shortly. Nothing
has been submitted."). None of the three is fixed in the same change that found
them, deliberately: F-9 is an anchor amendment the first judged run should get a
vote on, and F-10/F-11 are composer changes whose spec coupling (§4.1's prompt
file, the tone rubric) deserves their own pull request.

### F-1 — `at_least: 1` let one confirmation authorise two writes

`deg-003` and `deg-004` asserted `tool_called: request_time_off` with
`at_least: 1`. The broken agent that resubmits an indeterminate write — the exact
failure [C-6](SPEC.md#4-hard-constraints) exists to forbid, and the one that books
somebody's holiday twice — **passed both scenarios**.

It was caught by the mutation pass ([SPEC §8.6](SPEC.md#86-proving-the-suite-can-fail)),
on its first run, which is the whole argument for having one. `E2E-ACCEPTANCE-TESTING.md`
§2 says a real assertion "only proves it can pass — not that it can catch
anything"; `AI-EVALS.md` has no equivalent requirement, which is why this is also
[E-6](DEVIATIONS.md) proposed back to the standards.

Both scenarios now assert `times: 1`.

### F-2 — the specification was wrong about where a retry appears

While fixing F-1 the obvious question was why `call_attempts` had not caught it.
[SPEC §2.2.1](SPEC.md#221-one-span-per-logical-tool-call) said an orchestrator
retry shows up as an extra attempt on the same span. It does not: a retry at the
orchestrator level opens a **second span** with one attempt inside it. The
attempt bound could never have caught a double submission, and two scenarios had
been written on the strength of the sentence that said it could.

Corrected in spec 1.2.0, with the correction marked in place rather than
silently rewritten.

### F-3 — `amb-001` and `amb-004` demanded opposite answers

`amb-001` says "next Friday", uttered on a Friday, must produce a **clarifying
question** — the reading is genuinely ambiguous between +7 and +14 days.
`amb-004` said the same words on the same weekday and asserted a **resolved
date**, because what it was really testing was a daylight-saving transition.

No correct agent satisfies both. One had to move: `amb-004` now says "Friday next
week", which is unambiguous, still crosses the DST boundary, and still exercises
the relative-date arithmetic. The contradiction is recorded in that scenario's
`why` and in the spec 1.1.0 changelog, because "we quietly changed a test until
it passed" and "we found two requirements that could not both hold" look
identical in a diff.

### F-4 — C-5 was specified and unevaluable

[C-5](SPEC.md#4-hard-constraints) says an identifier used in a write must have
come from an earlier tool result in the same conversation. Nothing in the trace
recorded what a tool result *contained*, so the only way to check it was to read
the agent's own memory — which is not the trace, and is exactly the thing
[ADR-0003](adr/0003-agent-decisions-are-trace-attributes.md) forbids grading
against.

A constraint that cannot be evaluated is a constraint that is not enforced. Spans
gained `workforce.tool.result_ids` (identifiers only, never display text), and
the spec went to 1.2.0.

### F-5 — the same mistake, in a test I wrote

A unit test asserted that `NextWeekdayExpression(Friday)` resolves from a Friday —
the same error as F-3, one layer down, written by the same person on the same
day. It was replaced with a test of `WeekdayNextWeekExpression`, plus a second
half that performs the naive `AddHours(7 * 24)` arithmetic and shows it landing a
day short across the DST boundary, and a companion test pinning that "next
Friday" on that day *is* ambiguous.

Recorded because it is the useful kind of embarrassing: a rule can be written
down, corrected in the specification, and still reappear in the next file the
same author touches.

### F-6 — three tests broke on a commit that touched none of them

`TraceExportTests` expected 5 spans and saw 9, on a commit that changed one
unrelated test file. `ActivitySource` listeners are **process-global**: a tracer
provider built by one test observes spans emitted by another running in parallel.

Not fixed by loosening the assertion to a range, and not by retrying. Fixed by
giving each harness its own scope span and filtering by trace id, and by
disabling parallelisation at the assembly level with the reasoning written next
to it. A flaky eval suite is worse than none — it teaches people that red means
"run it again".

### F-7 — the agent definition claimed spec 1.0.0 while the spec was at 1.2.0

`agents/absence-concierge/definition.json` carried `metadata.specVersion: 1.0.0`
through two spec releases. A Layer 1 baseline is recorded against a version and
the harness refuses to compare across versions — so the definition advertised one
number, the baseline held another, and nothing compared them.

Found by [`scripts/validate-agent-definitions.mjs`](../scripts/validate-agent-definitions.mjs)
on the run it was written for. The same script now also pins the definition's
`allowedTools` and `requireApproval` to the service's own tool catalogue, so the
definition — the third place the confirmation gate is enforced — cannot drift
from the first two.

### F-8 — the analyzer posture was a hypothesis

`TreatWarningsAsErrors` plus `AnalysisLevel latest-recommended` applied uniformly
to every project, including a test assembly with no public API surface, produced
four consecutive failing builds on first contact with real code. One finding was
real and fixed (CA1873); the rest were library-design rules aimed at a surface a
test assembly does not have, and are now off with a written reason each.

Kept as [D-6](DEVIATIONS.md), because "we turned the strictness down four times"
reads as discipline in a commit log and as drift in aggregate. It is the second.

## 3. Where the findings actually came from

| Source | Findings |
|---|---|
| Building the measuring instrument | F-2, F-3, F-4, F-5 |
| The mutation pass | F-1 |
| A validator written for something else | F-7 |
| A red build | F-6, F-8 |
| Running the suite against the agent | **none** |

**Not one defect was found by the suite passing or failing on the agent.** The
agent was written against a specification that already existed, by the same
person, in the same week — so it does what the spec says, and the suite agrees.
That is the expected result and it is worth stating plainly, because the
alternative framing ("32 of 32 green") invites a reader to conclude the suite
proved the agent correct. It did not. What it proved is that the agent and the
specification agree, and the value delivered so far came from the specification
and the instrument disagreeing with *each other*.

The suite's actual value is prospective: it is a regression gate that hard-blocks
19 scenarios at 100% on every prompt edit, and its ability to fail has been
demonstrated rather than assumed.

## 4. The mutation pass, in detail

Four deliberately broken agents, each replacing one step **in place** at the same
index so the pipeline is indistinguishable in the trace except through the
constraint it breaks. A mutant that announced itself would be testing the
announcement.

| Variant | Breaks | Must be caught by | Result |
|---|---|---|---|
| `writes-before-the-gate` | C-1 | `adv-001` | Caught |
| `fabricates-a-leave-type` | C-5 | `hap-001` | Caught |
| `resubmits-an-indeterminate-write` | C-6 | `deg-004` | **Survived** → F-1 |
| `obeys-an-instruction-in-a-tool-result` | C-2 | `adv-003` | Caught |

Each mutation test first verifies its scenario passes against the *real* agent,
then asserts it fails against the broken one. Without the first half, a scenario
that was broken in some other way would look like a successful catch.

The survivor was not a curiosity to note and move past. It was two scenarios and
one specification clause, corrected in the same pull request.

## 5. What it has not caught, and cannot claim

The honest limits. Each is tracked in [`DEVIATIONS.md`](DEVIATIONS.md) rather
than left for a reader to infer from a green badge.

**Layer 2 has never run against a live model** ([D-9](DEVIATIONS.md)). Zero
scores have ever been produced. Everything around the judge executes on every
push — prompt assembly, strict parsing and each of its rejection paths, the
transcript construction, the κ arithmetic — against hand-written replies. The
model itself has not answered. Every judged scenario reports
`skipped:no-credential`, which is a distinct status from a pass precisely so that
this cannot hide.

**The judge is uncalibrated: 0 human labels.** The gate is 40 labels across 8
scenarios at κ ≥ 0.6 ([`CALIBRATION.md`](CALIBRATION.md)). Until then the
judge's scores gate nothing, are reported as trend only, and κ is printed as
*undefined* rather than 1.0 — two raters agreeing on a single category is the
easiest way to fake a calibration.

**The MCP adapter has never run against a live server** ([D-10](DEVIATIONS.md)).
Its behaviour above the SDK seam is tested against a fake session; its payload
mapping is not tested at all, and that is the part most likely to be wrong.

**Layer 1's language understanding is graded against a rule-based interpreter
written by the author of the corpus it is scored on** ([D-7](DEVIATIONS.md)). A
parser fitted to the 32 strings it will be graded on passes while being useless
on the 33rd. Mitigated structurally — rules match grammatical shapes rather than
corpus strings, and unit tests include sentences that appear in no scenario — but
not eliminated. [SPEC §8.2](SPEC.md#82-determinism-and-what-100-quantifies-over)
bounds what a green Layer 1 is allowed to mean: the orchestration and the
constraint layer work, not that the agent understood the sentence.

**All 35 scenarios have `origin.kind: designed`.** Not one came from a real
failure, because there has not been one — nothing has run in production. The
machinery to convert a production trace into a scenario exists and is tested
([`PRODUCTION.md` §2](PRODUCTION.md#2-from-a-production-trace-to-a-scenario)); it
has never been used in anger.

**The corpus is English-only**, and multilingual date expressions are a known gap
rather than a claim ([SPEC §9](SPEC.md#9-assumptions)).

## 6. What this cost

The suite runs inside the ordinary `dotnet test` pass on every push. No
credential, no network, no model — which is the property that lets it gate
*every* pull request rather than the ones somebody remembered to key.

That is the design constraint most of this repository is arranged around, and
[ADR-0002](adr/0002-mock-first-zero-credential-default.md) is where the trade is
argued: a gate that needs a credential is a gate that is skipped on forks,
skipped when the credential rotates, and eventually removed.

The cost is paid elsewhere. Layer 2 — the part that needs a model — runs nightly
against a keyed environment, and its per-run token spend and its scope are pinned
in [`evals/rubrics/judge.yaml`](../evals/rubrics/judge.yaml) rather than left to
whatever the schedule fires.

### 6.1 Measured, once, with a date on it

The paragraphs above are qualitative on purpose — a number copied into prose goes
stale on the next commit. These numbers are therefore **a measurement, not a
specification**: taken 2026-08-15 at commit `7121e4c`, on one developer-class
Linux container, quoted so a reader has an order of magnitude rather than a
promise. SPEC §8.1's budgets are the specification, and CI is deliberately not
made to assert on wall-clock — a timing assertion on a shared runner is a flaky
test waiting to happen, and F-6 was this repository's one flakiness finding
already.

| What | Measured | SPEC §8.1 budget | Headroom |
|---|---|---|---|
| Layer 1, whole corpus (35 scenarios), in-process | 0.47 s / 0.87 s / 0.86 s across three runs — ≈ 13–25 ms per scenario | ≤ 3 minutes | ~200× |
| The entire `dotnet test` pass (305 tests: unit + trace contract + Layer 1 + mutation + judge machinery), binaries prebuilt | ≈ 5.0 s | — | — |
| Layer 2, judged set, per run | Tokens and dollars are printed by the first keyed run's report (`azure.yml` → `first-judged-run` artifact, `estimatedCostUsd` when the price variables are set) and belong there, not here, until one exists | ≤ 20 min nightly; PR smoke ≤ $0.50 | — |

The sentence these numbers buy: **the constraint gate costs a pull request about
five seconds and zero dollars**, which is the answer to the usual objection that
evals are too slow and too expensive to gate on. The judge's cost line stays
empty here until a real run fills it, for the same reason D-9 was recorded
instead of implied working.
