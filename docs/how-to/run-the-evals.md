# How to run the evals

> 🇵🇱 [Wersja polska](run-the-evals.pl.md) · ⬅ [Start here](../START-HERE.md)

## Everything, the way CI does it

```bash
dotnet test
```

No credential, no network, no model. This is the same command the merge gate and
the release gate run, so a green result locally means the same thing it means in
CI.

## Just the deterministic layer

Layer 1 asserts over the trace. It is the fast one.

```bash
dotnet test --filter "FullyQualifiedName~Layer1"
```

## Just the mutation pass

Runs the four deliberately broken agents and checks each is caught by the
scenario named as its catcher.

```bash
dotnet test --filter "FullyQualifiedName~MutationTests"
```

## Just the judge

```bash
dotnet test --filter "FullyQualifiedName~Layer2"
```

Without a credential every judged scenario reports `skipped:no-credential` and
the test is skipped, not passed. To make it actually run, see
[How to enable the judge](enable-the-judge.md).

## One scenario

Scenario names appear in the test output. Filter on the id:

```bash
dotnet test --filter "FullyQualifiedName~Layer1"
```

then search the output for the id. There is no per-scenario test filter, because
scenarios are data rather than test methods.

## Validate the corpus without running the agent

Schema, ids, filenames, gating and assertion discipline — no build required:

```bash
npm run validate:scenarios
```

Use this while writing a scenario. It is much faster than `dotnet test` and
catches every structural mistake.

## Everything the documentation gates check

```bash
npm run lint
```

Runs markdownlint, the relative-link check, the diagram-pairing check, the
scenario validator and the agent-definition validator — the whole `lint-docs` CI
job.

## Where the results are written

| Path | What is in it |
|---|---|
| `TestResults/eval-report.json` | The full run: per-scenario results, timings, judge verdicts |
| `evals/baselines/layer1.json` | The recorded baseline behaviour scenarios are compared against |

On a pull request the same data is rendered into one sticky comment carrying the
diff against the baseline, rather than a dashboard.

## Reading the gates

| Class of scenario | Rule |
|---|---|
| `constraint`-gated | 100%. Any failure blocks the merge. No exceptions, no "flaky" |
| `behaviour`-gated | Pass rate at or above the recorded baseline |
| Judge criteria | Per-criterion threshold; `grounding` also has a floor no single score may fall below |

## If the whole suite fails immediately

Check the timezone first. The service refuses to start if `Europe/Madrid` is not
available on the machine, and it does that deliberately rather than falling back
to UTC — a fallback would resolve every date in the wrong frame while every test
still passed. Install `tzdata`.
