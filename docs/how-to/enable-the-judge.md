# How to enable the judge

Layer 2 grades what Layer 1 structurally cannot: whether the reply is clear,
honest, grounded and in the right register. Without a credential it reports
`skipped:no-credential` — an explicit skip, never a silent green.

> 🇵🇱 [Wersja polska](enable-the-judge.pl.md) · ⬅ [Start here](../START-HERE.md)

## Locally

Set three values. Locally they come from `dotnet user-secrets`, never a file in
the repository:

```bash
cd src/AbsenceConcierge.AgentService
dotnet user-secrets set "Llm:Provider"   "AzureOpenAI"
dotnet user-secrets set "Llm:Endpoint"   "https://<your-resource>.openai.azure.com"
dotnet user-secrets set "Llm:JudgeModel" "<your-deployment-name>"
dotnet user-secrets set "Llm:ApiKey"     "<key>"
```

**`Llm:Model` and `Llm:JudgeModel` are deployment names, not model ids.**
Conflating them is the usual way a first Azure OpenAI call fails.

Then:

```bash
dotnet test --filter-query "/*/*/*Layer2*/*"
```

## In CI

The nightly workflow reads them from the `evals` GitHub environment:

| Variable | Purpose |
|---|---|
| `Llm__Provider` | `AzureOpenAI` |
| `Llm__Endpoint` | The resource endpoint |
| `Llm__JudgeModel` | The deployment serving the judge |
| `Llm__ApiKey` | The key |
| `Llm__PricePerMillionInputTokens` | Optional — lets the report state a cost rather than a token count |
| `Llm__PricePerMillionOutputTokens` | As above |

`nightly.yml` runs at 02:30 UTC with scope `full`, and a test asserts that it
does — the keyed run is not optional. On pull requests the scope is `smoke`.

## Pin the judge separately from the agent

`Llm:JudgeModel` is deliberately separate from `Llm:Model`. If both moved
together a changed score could not be attributed, because both sides of the
comparison would have moved at once (ADR-0004).

The same ADR forbids a silent fallback: a run that could not reach the pinned
model and quietly answered with another would record a number describing a system
nobody chose. A fallback is permitted **only** when the provider reports the model
that actually answered and the caller records it on the span — and a baseline is
partitioned by the model that produced it.

## Before its scores may block anything

Calibration comes first. The gate is defined in `evals/rubrics/judge.yaml`:

| Requirement | Value |
|---|---|
| Minimum labels | 40 |
| Minimum scenarios labelled | 8 |
| Minimum agreement (kappa) | 0.6 |

A judge that has never been compared against a human is an opinion with a number
attached. The protocol, and what its first run found, are in
[`CALIBRATION.md`](../CALIBRATION.md) — labelling produced three defects
([F-9, F-10, F-11](../FINDINGS.md)) that no suite run had ever surfaced, because
it forced somebody to read every transcript against every anchor.

## What the judge will refuse to accept

`RubricJudge.Parse` is strict, and every rejection is reported as a **judge
failure** rather than a low score:

- prose instead of a JSON object
- a score outside its rubric's scale
- a missing criterion — a missing score is not a zero and not a pass
- a criterion nobody asked for — a judge inventing criteria has stopped following
  the rubric file, and the rubric file is the pin
- a score with no justification — an unjustified score cannot be reviewed, and
  calibration is a review

## Editing a rubric

Changing `judge.yaml` or `judge-prompt.md` changes the instrument. Both are
SHA-256 hashed into every report, and a CI check fails a pull request that edits
them without bumping the suite version — a score compared across that edit is a
measuring stick that changed length between readings.

## Cost

`SPEC.md` §8.1 budgets Layer 2 in money as well as minutes, and the report meters
input and output tokens per run. Set the two price variables if you want the
report to state currency instead of tokens.
