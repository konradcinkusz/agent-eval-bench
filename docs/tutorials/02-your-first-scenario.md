# Tutorial 2 — Your first scenario

In [Tutorial 1](01-first-run.md) you watched the agent stop, by hand. In this
lesson you will make the machine watch it for you — permanently, on every future
change to the repository.

You will write a scenario, watch it **fail**, and then make it **pass**. That
loop is what this whole repository exists to support.

**You need:** the working setup from Tutorial 1, and about 25 minutes.
**You do not need:** any credential. Everything here runs offline.

> 🇵🇱 [Wersja polska](02-your-first-scenario.pl.md) · ⬅ [Start here](../START-HERE.md)

## What a scenario is

A scenario is a **file of data, not code**. It says three things: what world the
agent is in, what was said to it, and what must be true afterwards.

Because it is data, it is readable by someone who does not write C#, and it would
port to a Ruby or TypeScript implementation of the same agent unchanged.

## Step 1 — Start from a working example

Copy the single-day vacation scenario:

```bash
cp evals/scenarios/happy/hap-003-single-day-vacation-friday.yaml \
   evals/scenarios/happy/hap-009-single-day-vacation-thursday.yaml
```

Open the new file. You are going to turn "Friday" into "Thursday".

## Step 2 — Make it yours

Change four things. Leave everything else alone.

**The id**, which must match the filename:

```yaml
id: hap-009-single-day-vacation-thursday
```

**The title and the reason it exists.** The `why` is not decoration — it is the
part a reviewer reads in a year when the scenario breaks and they must decide
whether the scenario or the agent is wrong:

```yaml
title: A single day of vacation, asked for on the Tuesday before

why: >-
  The same off-by-one risk as hap-003, one day earlier in the week. Asked on a
  Tuesday, "Thursday" is two days away, and start and end must be the same date.
  An agent that adds a day to `end_date` books two days of somebody's allowance
  without either of them noticing.
```

**The sentence the user says:**

```yaml
conversation:
  - role: user
    content: Book me Thursday off
  - role: confirmation
    decision: approve
    content: Yes, go ahead
```

**The dates you expect.** Here is the deliberate mistake — put Friday's date in,
exactly as a tired person would:

```yaml
  - assert: tool_called_with
    tool: request_time_off
    match: subset
    args:
      leave_type_id: lt-201
      start_date: '2026-08-13'
      end_date: '2026-08-14'
```

## Step 3 — Check the file is well-formed

```bash
npm run validate:scenarios
```

This checks the schema and the corpus rules — it does **not** run the agent. You
should see it count your file:

```text
validate-scenarios: 36 scenarios valid.
```

If you mistyped the id or the filename, it will say so precisely. It is strict on
purpose: a scenario nobody can load is a test that silently does not exist.

## Step 4 — Run it, and watch it fail

```bash
dotnet test
```

Your scenario runs the **real agent** — the same pipeline the demo used in
Tutorial 1 — against the fixture world, with the clock pinned to that Tuesday.

It fails. The failure names your scenario and the assertion that did not hold:
`request_time_off` was called with `end_date` of `2026-08-13`, and you asserted
`2026-08-14`.

**Read that carefully, because it is the point of the exercise.** You wrote down
what you believed. The agent did something else. Exactly one of you is wrong, and
now there is a machine that will not let the disagreement pass silently.

## Step 5 — Decide who is wrong

In this case, you are. The clock is pinned to Tuesday 11 August 2026, "Thursday"
is the 13th, and a single day starts and ends on the same date.

Fix it:

```yaml
      start_date: '2026-08-13'
      end_date: '2026-08-13'
```

## Step 6 — Green

```bash
dotnet test
```

Your scenario passes. From now on it runs on **every push**, and any future
change that makes the agent book two days for a one-day request will fail this
scenario before it can merge.

You have just added a permanent, mechanical guarantee to the repository. That is
the whole loop.

## Step 7 — Prove the scenario can actually catch something

A test that has only ever passed proves nothing. Break the thing it guards and
watch it go red.

Change your assertion back to `end_date: '2026-08-14'`, run `dotnet test`, and
confirm it fails. Then change it back.

That habit has a formal name here — the **mutation pass** — and the repository
runs it against four deliberately broken agents on every push. On its very first
run it found a real hole: two scenarios asserted `at_least: 1` on the write
instead of `times: 1`, so a broken agent that submitted the same request **twice**
passed both of them. That is [F-1](../FINDINGS.md).

## Step 8 — Meet the rule that makes refusals real

Your scenario is a happy path. Refusals have an extra rule, and it is worth
seeing now.

If you write a `denied` or `adversarial` scenario that asserts a refusal happened
but does not assert that the forbidden call *did not*, the validator refuses the
file:

```text
class "denied" requires at least one absence assertion (tool_not_called or
event_not_emitted) — a refusal asserted without asserting the absence of the
attempted call is half a test
```

An agent that refuses politely and calls the tool anyway would pass the other
half. About one assertion in five in this corpus asserts that something did
**not** happen, and the ratio is enforced rather than hoped for.

## What you learned

| | |
|---|---|
| A scenario is data | Readable without C#, portable to another stack |
| `why` matters as much as `expect` | It is what a future reviewer uses to decide who is wrong |
| Red first, then green | The failure is the evidence that the scenario measures something |
| A test that never failed is untested | Break it on purpose — the repository does this formally |
| Refusals need two assertions | "It refused" and "it did not call" are different claims |

## Clean up, or keep it

If you want to keep your scenario, it needs a mention in
[`docs/SPEC.md` §3](../SPEC.md) so every scenario traces to a stated behaviour —
see [How to add a scenario](../how-to/add-a-scenario.md). Otherwise:

```bash
rm evals/scenarios/happy/hap-009-single-day-vacation-thursday.yaml
```

## Where to go next

- [How to add a scenario](../how-to/add-a-scenario.md) — the full checklist, including the parts this tutorial skipped
- [How to add a behaviour](../how-to/add-a-behaviour.md) — when the agent genuinely cannot do the thing yet
- [How to debug a failing scenario](../how-to/debug-a-failing-scenario.md) — when the disagreement is not obvious
- [`DIAGRAMS.md` C1–C2](../DIAGRAMS.md) — the measuring loop, and what an assertion actually reads
