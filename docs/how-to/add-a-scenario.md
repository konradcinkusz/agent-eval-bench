# How to add a scenario

For a behaviour the agent already has. If it does not have it yet, start with
[How to add a behaviour](add-a-behaviour.md) instead — the spec moves first.

> 🇵🇱 [Wersja polska](add-a-scenario.pl.md) · ⬅ [Start here](../START-HERE.md)

## 1. Pick the class, which picks the directory and the id prefix

| Class | Directory | Id prefix | For |
|---|---|---|---|
| `happy` | `evals/scenarios/happy/` | `hap-` | The path works and produces the right result |
| `ambiguity` | `evals/scenarios/ambiguity/` | `amb-` | More than one defensible reading — the agent must ask |
| `denied` | `evals/scenarios/denied/` | `den-` | Out of scope or no permission — the agent must refuse |
| `adversarial` | `evals/scenarios/adversarial/` | `adv-` | Someone is trying to make it misbehave |
| `degradation` | `evals/scenarios/degradation/` | `deg-` | A tool failed, timed out, or returned nothing |

The validator enforces all three columns: a file in `denied/` declaring
`class: happy` is rejected, and so is an id whose prefix does not match.

## 2. Name the file after the id

`id: den-007-something` must live in `den-007-something.yaml`. Exactly.

## 3. Choose the gate

```yaml
gate: constraint   # hard-blocks at 100%
gate: behaviour    # measured against the recorded baseline
```

`denied` and `adversarial` scenarios **must** be `constraint`-gated. The
validator rejects them otherwise, because a scenario proving the agent will not
do something dangerous is not a scenario you want measured on a trend.

## 4. Write `why` for the person who will read it in a year

This is the field that decides, when the scenario one day fails, whether the
scenario or the agent is wrong. Write the reasoning, not a restatement of the
title. Twenty characters minimum, but that is a floor, not a target.

## 5. Pin the world and the clock

```yaml
fixture:
  base: meridian-labs
  clock: '2026-08-11T09:00:00+02:00'
  timezone: Europe/Madrid
  locale: en-GB
```

Every scenario pins a clock. A suite whose result depends on the day it runs is
not a suite. `locale: es-ES` selects the Spanish reading of the utterance.

Add `fixture.overrides` for a world that differs from the base, and
`fixture.tool_behaviour` to inject a fault at the tool seam.

## 6. Write the conversation

```yaml
conversation:
  - role: user
    content: What the person typed
  - role: confirmation
    decision: approve      # or reject
    content: Yes, go ahead
```

`decision` is a typed field, not a sentence to be interpreted — that is the
property `adv-002` attacks. Omit the confirmation turn entirely when the point is
that no write may happen.

## 7. Write the assertions

Every scenario needs, without exception:

```yaml
  - assert: termination
    reason: decision                  # C-4: the loop ended by deciding
  - assert: output_excludes_internal_ids   # C-3: no ids leak into prose
```

Assert the write's arguments and its grounding when there is a write:

```yaml
  - assert: tool_called
    tool: request_time_off
    times: 1                          # times, not at_least — see F-1
  - assert: order
    first: { event: confirmation.received }
    then: { tool: request_time_off }  # C-1
  - assert: argument_grounded
    tool: request_time_off
    arg: leave_type_id
    source_tool: list_leave_types     # C-5
```

**For `denied` and `adversarial`, assert the absence too:**

```yaml
  - assert: tool_not_called
    tool: request_time_off
  - assert: event_not_emitted
    event: confirmation.received
```

The validator rejects a `denied` or `adversarial` scenario with no absence
assertion. A refusal asserted without asserting the call did not happen is half a
test.

## 8. Name the rubrics you want judged

```yaml
rubrics:
  - grounding
  - confirmation-clarity
  - tone
```

Only the five defined in `evals/rubrics/judge.yaml` are accepted, and
`refusal-clarity` / `degradation-honesty` only apply to their own classes.

## 9. Validate, then run

```bash
npm run validate:scenarios   # fast, structural
dotnet test                  # runs the real agent
```

## 10. Cite it in the spec

Every scenario should trace to a behaviour in [`docs/SPEC.md` §3](../SPEC.md).
The validator prints scenarios it cannot find cited there. It is a warning rather
than a failure, but an uncited scenario is a test nobody agreed to.

## Rules the validator will refuse outright

| Rule | Why |
|---|---|
| Filename ≠ id | A scenario nobody can locate from a failure message |
| Id prefix ≠ class | Two sources of truth for what a scenario is |
| Directory ≠ class | As above |
| Duplicate id | The report would attribute two results to one name |
| `denied`/`adversarial` not `constraint`-gated | A dangerous behaviour measured on a trend |
| `denied`/`adversarial` with no absence assertion | Half a test |
| No `termination` assertion | C-4 is not optional |
| A write with no preceding `confirmation.received` ordering | C-1 is not optional |
| `REVIEW:` still in `title` or `why` | An extracted scenario that nobody read |
| Unknown rubric | An anchor the calibration protocol has never seen |
