# evals/

The scenario corpus, the schema it must satisfy, and the fixtures it runs
against. **Scenarios are data, not code** — the format matters less than that
fact, but the format is pinned so a mistyped key fails loudly.

The harness that executes these arrives in Phase 4. What runs today is
`npm run validate:scenarios`, wired into CI in the same pull request that created
the corpus — because *"an unreferenced test config is not a latent capability; it
is documentation that lies"* (TESTING-STRATEGY.md §9).

```text
evals/
  schema/scenario.schema.json   the contract, strict (additionalProperties: false)
  fixtures/                     shared worlds; scenarios name one and write only the delta
  scenarios/
    happy/        the behaviours the spec promises
    ambiguity/    underspecified input — expect a question, not a guess
    denied/       refusals, each asserted twice
    adversarial/  injection, through user input AND through tool results
    degradation/  timeouts, 5xx, empty results
```

## The five classes, and why each exists

| Class | Count | What it protects |
|---|---|---|
| `happy` | 6 | The paths the spec promises. If these break, nothing else matters |
| `ambiguity` | 8 | Dates and names with more than one defensible reading. The largest class on purpose — it is where a confident wrong answer is cheapest to produce |
| `denied` | 6 | Every out-of-scope rule, asserted as a refusal *and* an absence |
| `adversarial` | 7 | Prompt injection. Four through tool results, three through user input |
| `degradation` | 5 | Tool timeouts, 5xx, and empty successes |

A suite with no adversarial class is testing the demo, not the product. The same
argument applies to every other class, so the validator fails if any of the five
is empty.

## Reading a scenario

Six fields carry the meaning:

- **`why`** — what breaks if this scenario stops passing. Read this first; a
  scenario that cannot answer it is decoration.
- **`gate`** — `constraint` hard-blocks at 100%; `behaviour` is measured against
  the recorded baseline.
- **`fixture`** — the world, with `clock` and `timezone` **required**. A scenario
  that passes only in August is not a scenario.
- **`conversation`** — user turns, plus `confirmation` turns. A confirmation is a
  separate role, not a chat message, so a plausible-sounding sentence can never
  stand in for an explicit approval.
- **`expect`** — assertions over the trace. Never over reply text.
- **`rubrics`** — the Layer-2 criteria that apply, if any.

## Constraint assertions are sticky

`gate` describes how the **scenario** is measured. But an assertion that encodes
a hard constraint from [`docs/SPEC.md`](../docs/SPEC.md#4-hard-constraints) —
ordering around a write, the absence of a write, grounding of an id,
`output_excludes_internal_ids`, `termination` — hard-blocks **wherever it
appears**, including inside a scenario gated as `behaviour`.

A constraint violated on a happy path is still a constraint violation. Gating it
softly because it turned up in a soft scenario would be the loophole that makes
the whole gate advisory.

## What the validator enforces

Beyond the schema, `scripts/validate-scenarios.mjs` refuses to pass:

- an **empty corpus**, or any class with no scenarios;
- duplicate ids, or an id that disagrees with its filename or directory;
- a `denied` or `adversarial` scenario with **no absence assertion** — the
  two-assertion rule, enforced mechanically rather than by review;
- a `denied` or `adversarial` scenario not gated as `constraint`;
- any scenario missing `termination` or `output_excludes_internal_ids`;
- a scenario that asserts a **write** without also asserting that
  `confirmation.received` preceded it and that the leave type id was grounded;
- a `fixture.base` naming a file that does not exist;
- an unknown rubric id;
- a citation in `docs/SPEC.md` pointing at a scenario id that does not exist.

**What it cannot catch**, said plainly: a citation aimed at a scenario that
exists and tests something else. Four of those were found while writing this
corpus — by reading the pairs, not by any script. A green validator is evidence
of what it checks and of nothing more.

## Adding a scenario

1. Pick the class. If it fits none of the five, that is worth discussing before
   writing it.
1. Copy the nearest existing file. `happy/hap-001` and
   `adversarial/adv-003` are the two style references.
1. Write `why` first. If it is hard to write, the scenario is not yet a scenario.
1. Pin a clock. Verify the day of week — several scenarios here turn on it.
1. Run `npm run validate:scenarios`.
1. If it proves a behaviour in `docs/SPEC.md` §3, add it to that row's citation
   list. If it proves something the spec does not claim, amend the spec first.

Scenarios born from a real failure carry `origin.kind: production-trace` or
`incident`, and they are worth more than designed ones. Every production incident
becomes a scenario before it becomes a fix.

## What is not here yet

- **`baselines/`** — Phase 4. Until a baseline is recorded, "pass rate ≥
  baseline" has nothing to compare against.
- **`rubrics/`** — Phase 5. The criteria and their anchors currently live in
  `docs/SPEC.md` §5; when the versioned rubric files land, the spec cites them
  rather than restating them, and `KNOWN_RUBRICS` in the validator is deleted
  rather than left to drift alongside.
- **The harness itself** — Phase 4. Today these files are validated, not
  executed. That distinction is stated everywhere it matters, because a corpus
  that has never run is a corpus whose assertions are untested.
