# How to debug a failing scenario

> 🇵🇱 [Wersja polska](debug-a-failing-scenario.pl.md) · ⬅ [Start here](../START-HERE.md)

## First, answer the only question that matters

**Is the scenario wrong, or is the agent wrong?**

Read the scenario's `why` field before you read any code. That field exists
precisely for this moment: it records what the author believed and why. If the
`why` still describes behaviour you agree with, the agent is wrong. If it does
not, the scenario is.

Do not skip this. Changing an assertion until it passes is how a regression gets
recorded as expected behaviour.

## Read the trace, not the reply

Layer 1 asserts over spans and events, never over prose. So the reply telling you
something happened is not evidence that it happened.

```bash
dotnet test --filter-query "/*/*/*Layer1*/*"
```

The failure names the scenario, the assertion, and what the trace actually
contained.

## Failures by shape

### `tool_called` expected N, got M

The agent called a tool a different number of times. If M is larger and the tool
is `request_time_off`, stop and treat it as serious: that is C-6, and it means
somebody's leave could be booked twice.

Check whether a retry is involved. A retry at the orchestrator level opens a
**second span**, not a second attempt inside one span — that distinction is
[F-2](../FINDINGS.md), and getting it backwards is what let the double-write
defect hide.

### `order` failed — the write preceded the confirmation

This is C-1, the constraint the whole repository is built around. Something let a
write happen without a recorded human decision in the same trace. Look at whether
your step registered before `ConfirmationGateStep` in
`ServiceCollectionExtensions.cs`.

### `argument_grounded` failed

C-5: an identifier in the write did not appear in any earlier tool result in the
same trace. Either the agent invented it — a confidently plausible leave-type id
is the classic case — or the tool result was never recorded.

Check `workforce.tool.result_ids` is present on the read's span. If it is empty,
the problem is the instrumentation, not the agent. That was
[F-4](../FINDINGS.md): the constraint was specified and unevaluable because
nothing recorded what a tool returned.

### `output_excludes_internal_ids` failed

C-3: an internal id reached the user-visible text. Usually a reply template
interpolating something it should have named instead.

### `termination` expected `decision`, got `iteration_cap`

C-4: the turn ended by exhausting the step cap rather than by deciding. Either
the pipeline is longer than `MaxSteps` (32), or a step is looping.

### `termination` got `error`

The turn threw. The orchestrator catches it deliberately so the turn still
produces a graded outcome — otherwise the scenario would fail with "no outcome"
rather than with the failure that caused it. The exception is in the log.

### Everything fails at once

Check the timezone. The service refuses to start without `Europe/Madrid` and does
so deliberately rather than falling back to UTC.

## When the dates are wrong

Check the scenario's pinned clock first, and what weekday it is. Most date
failures are the scenario's arithmetic, not the agent's:

- Is the expected date computed from `fixture.clock`, in `fixture.timezone`?
- For a single day, do `start_date` and `end_date` match? Off-by-one lives here.
- Does the range cross a weekend, a holiday, a month boundary, or a
  daylight-saving transition? Each has its own scenario in `ambiguity/` — compare
  against the one that already passes.
- Is the phrase genuinely ambiguous? "Next Friday" said on a Friday must produce
  a clarifying question, not a resolved date. Asserting a date there is asserting
  the wrong behaviour.

## When the judge fails rather than scores low

An unreadable verdict is reported as a **judge failure**, which is a different
fact from a low score and is never averaged in as a zero. The parser rejects
prose instead of JSON, a score outside its rubric's scale, a missing criterion,
a criterion nobody asked for, and a score with no justification. The message says
which.

## Isolate it

`ScenarioRunner` gives every scenario a fresh service provider, a fresh token
store, a fresh conversation store and a world rebuilt from the fixture. Nothing
survives between scenarios, so a failure that only appears in a full run and not
alone is a bug worth reporting rather than a flake to re-run.

The unit test project runs with parallelism disabled for a related reason:
`ActivitySource` is process-global, and parallel tests stole each other's spans.
That was [F-6](../FINDINGS.md) — three tests "broke" on a commit that touched
none of them.
