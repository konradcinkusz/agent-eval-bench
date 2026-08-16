# How to add a behaviour

When the agent genuinely cannot do the thing yet. The order below is not a
suggestion — a CI check enforces part of it.

> 🇵🇱 [Wersja polska](add-a-behaviour.pl.md) · ⬅ [Start here](../START-HERE.md)

## The order

```text
1. SPEC.md          say what the agent must do, and cite the scenario that will prove it
2. the scenario     write it; it fails, because the behaviour does not exist
3. the step         make it pass
4. the baseline     re-record, and put the diff in the pull request
```

Spec first is the method, not a scheduling preference: a prompt gets edited the
way configuration gets edited — casually — and the spec is what makes such an
edit reviewable.

## 1. Amend the spec

Add the behaviour to [`docs/SPEC.md`](../SPEC.md) §3 with the next free `B-`
number, one line, testable, naming the scenario that will prove it.

If it is a rule about what the agent must **never** do, it belongs in §4 as a
hard constraint instead — and a new hard constraint needs a scenario that fails
without it.

Bump the spec version in the header, add a row to the change table saying what
changed and why, and bump `version` in
[`agents/absence-concierge/definition.json`](../../agents/absence-concierge/definition.json)
to match. The validator checks that one version appears in all three places it is
written.

## 2. Write the scenario, and watch it fail

See [How to add a scenario](add-a-scenario.md). It must fail before you write any
code — a scenario that passes against an agent that cannot do the thing is a
scenario that measures nothing.

## 3. Add a step, not a prompt instruction

Behaviours live in the pipeline, not in prose. Create a class in
`src/AbsenceConcierge.AgentService/Agent/Steps/` implementing `IAgentStep`:

```csharp
public string Name => "your_step_name";        // appears in the trace
public bool AppliesTo(AgentTurnContext context) => …;   // when it runs
public ValueTask<StepSignal> ExecuteAsync(…);  // Continue or Stop
```

Register it in `ServiceCollectionExtensions.cs` **at the right position** — the
pipeline's order is the specification, and registration order is that order.

Record the decision on the trace using a constant from `AgentDiagnostics`. If
your behaviour needs a name the trace does not have yet, add it there — and
remember that renaming anything in that file is a breaking change to the eval
suite.

Two rules that catch most first attempts:

- **A refusal belongs before the reads.** `ScopeGuardStep` runs at position 4 so
  a refusal costs no tool calls.
- **The reply composer is not a step.** Rendering is not a decision, and it runs
  outside the loop so every path — including one that stopped early or threw —
  still produces a reply.

## 4. Re-record the baseline

Behaviour-gated scenarios are measured against `evals/baselines/layer1.json`.
When your new scenario starts passing, the baseline moves. Re-record it and
**put the diff in the pull request** — a hand-edited baseline is how a regression
merges as an "expected change", which is why `CODEOWNERS` calls that path out
separately.

## What CI will refuse

| Check | Rule |
|---|---|
| `coupling` | A change to `prompts/` or `agents/` with no change to `docs/SPEC.md` fails the pull request |
| `coupling` | A change to `evals/fixtures/` or `evals/rubrics/` with no version bump fails |
| `validate:agents` | The definition's version must match the spec's, in all three places |
| `validate:agents` | `allowedTools` and `requireApproval` must match the service's own read/write catalogue |
| `architecture` | Domain vocabulary in `ServiceDefaults` fails — the kernel stays plumbing |
| Layer 1 | Constraint scenarios at 100%, behaviour scenarios at or above baseline |

## If the behaviour needs a model

It probably does not. On the CI-gated path the interpreter is rule-based, and the
model may write the reply and nothing else — it runs after every decision has
been made and recorded.

If you are reaching for a model to make a *decision*, you are moving a behaviour
out of the pipeline and into prose, where no constraint can hold it. That is the
change this repository exists to argue against.
