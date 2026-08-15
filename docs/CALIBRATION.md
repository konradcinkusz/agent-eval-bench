# Calibrating the judge

> **Current position: 45 labels across 21 scenarios exist — written by an AI
> rater, before the judge had ever produced a score anywhere. The judge's scores
> are still reported and trended, and they gate nothing**: κ is computed on the
> first keyed run, and the gate additionally waits for human labels under the
> owner's own handle (see "Who labelled first, and why that is disclosed").
>
> That gating state is printed by every run and asserted by a test. It is the
> honest state, not a placeholder — and stating it plainly is the point of this
> document.

[`AI-EVALS.md`](https://github.com/konradcinkusz/architecture-standards/blob/main/docs/guides/AI-EVALS.md)
§5 requires an LLM judge to be calibrated against human labels before its scores
gate anything, and marks that rule **"not yet demonstrated in the estate"**. This
repository is meant to be the demonstration. What follows is the protocol, the
arithmetic, and the position it is currently in.

## Why an uncalibrated judge must not gate

A judge that gates without calibration blocks merges on a number nobody has ever
checked against a person. It will be confident, consistent, and possibly wrong in
the same direction every time — and consistency is exactly what makes that
invisible: the score does not move, so nothing looks broken.

The failure is worse than having no judge, because a threshold nobody trusts still
gets raised until it stops firing, and then it is decoration with a merge cost.

## The instrument, and what pins it

| Part | Where it lives | How it is pinned |
|---|---|---|
| Rubrics and anchors | [`evals/rubrics/judge.yaml`](../evals/rubrics/judge.yaml) | SHA-256, recorded in every report |
| Judge prompt | [`evals/rubrics/judge-prompt.md`](../evals/rubrics/judge-prompt.md) | SHA-256, recorded in every report |
| Model | Configuration (`Llm:JudgeModel`) | The model the service says actually answered is recorded, not the one configuration hoped for ([ADR-0004](adr/0004-pin-the-model-and-never-fall-back-silently.md)) |
| Human labels | [`evals/calibration/labels.jsonl`](../evals/calibration/labels.jsonl) | Append-only, one label per line |

**Editing a rubric is editing the instrument.** A score compared across that edit is
a measuring stick that changed length between readings — the same defect
`SPEC.md` §8.4 describes for a fixture, one layer up. The hashes are what make it
visible in a diff rather than invisible in a number.

## The anchors are what make this possible at all

"Rate this reply 1–10" cannot be calibrated, because two people asked to produce a
7 are not doing the same thing. Every level in `judge.yaml` names a behaviour a
second reader could agree or disagree with — *"contains a plausible claim with no
support in the trace"* is checkable; *"good grounding"* is not.

The harness enforces this mechanically: a rubric declaring a 0–3 scale with no
anchor for one of those levels fails to load, with the reason.

## The protocol

1. **Run Layer 2 with the judge configured.** It writes
   `TestResults/eval-report-layer2.json`, which carries a score and a one-sentence
   justification per criterion per scenario.
1. **Label the same scenarios by hand, without reading the judge's scores first.**
   Reading them first produces agreement with the judge rather than a measurement of
   it; the whole number collapses to anchoring bias, and it collapses upward.
1. **Append each label** to `evals/calibration/labels.jsonl`, one JSON object per
   line:

   ```json
   {"scenario":"hap-001-sick-today-and-tomorrow","rubric":"grounding","score":3,"labeller":"kc","date":"2026-08-15"}
   ```

   `labeller` is a handle, never a name or an email — this repository is public.
1. **Run again.** The report now carries exact agreement, agreement within one
   level, and Cohen's κ.
1. **Compare, and act on the disagreements rather than the average.** A criterion
   where the judge is systematically one level high is a criterion whose anchors are
   ambiguous; the fix is the anchor, not the threshold.

## The arithmetic, and why it is unweighted κ

Raw agreement flatters. If nine of ten replies deserve a 3 and both raters give 3
to everything, they agree 90% of the time and neither has demonstrated anything —
chance alone predicts it.

Cohen's κ corrects for that. It is **unweighted** here, deliberately: a weighted κ
gives partial credit for being one level out, and these anchors are written so that
one level out is a real disagreement. The difference between *"all claims traceable
but one restated imprecisely"* and *"a plausible claim with no support in the
trace"* is the entire grounding criterion, and a metric that treats them as nearly
the same is measuring something else.

Raw agreement is reported alongside anyway, because κ punishes a labeller for being
right in an unsurprising way, and both numbers are worth seeing.

**κ is reported as undefined, never as 1.0, when every pair falls in one category.**
Two raters who both scored everything 3 agree completely and have demonstrated
nothing. Returning a perfect score there would let a judge be certified by a
labeller who was not looking, and it is the single easiest way to fake a
calibration.

## The gate

From [`evals/rubrics/judge.yaml`](../evals/rubrics/judge.yaml):

| Condition | Required |
|---|---|
| Labels recorded | ≥ 40 |
| Distinct scenarios covered | ≥ 8 |
| Cohen's κ | ≥ 0.6 |

Until all three hold, Layer 2's scores are **reported and trended and gate
nothing**. Layer 1's constraints continue to hard-block throughout, because they are
deterministic properties of a trace and need no judge at all.

The numbers are deliberately modest — roughly an hour of one person's attention. A
threshold nobody will ever reach is a threshold that quietly becomes permission to
skip calibration entirely, which is the outcome this document exists to prevent.

## Who labelled first, and why that is disclosed

The first 45 labels (2026-08-15, handle `claude-fable-5`) were written by an AI
assistant — Anthropic's Claude, a different model family from the judge — working
from the exact transcripts the judge reads (`TestResults/narratives/`, dumped by
`NarrativeDumpTests`) against the anchors in `judge.yaml`. The one unbreakable
rule was satisfiable by construction: when they were written, the judge had never
produced a score anywhere, so there was nothing to be anchored by.

What that set is evidence of, and what it is not:

- **It is** the protocol demonstrated end to end — the format, the dump utility,
  the κ arithmetic wired to real labels — and it is a real second reading of
  every transcript, from a rater with no stake in the judge agreeing.
- **It is not** a human label set, and `AI-EVALS.md` §5 says *human*. So the
  numeric gate above (40 / 8 / κ ≥ 0.6) is necessary but no longer sufficient:
  **judge scores do not gate until labels under the owner's own handle exist**,
  appended to the same file under the same rules. An AI-calibrated AI judge
  would be turtles most of the way down, and saying so here is cheaper than a
  reader discovering it.

The labelling itself surfaced three findings before any judge ever ran — which is
the protocol earning its keep on its first use (the disagreements were supposed
to be the interesting part, and they were, even with only one rater):

1. **The holiday's name is not in the trace.** `hap-006`'s reply says "Monday 12
   October (National Day, a company holiday)"; the trace's `excluded_days` says
   `2026-10-12=holiday` and never names it. Labelled `grounding: 2` — the
   strictest reading of the anchor — and left for the judge to disagree with:
   whichever way the first keyed run scores it, the anchor's wording about
   world-data the trace summarises but does not carry needs a sentence.
   `amb-008` ("Assumption") is the same case and was labelled the same way.
1. **The composer answers Spanish in English.** `hap-007`, `hap-008` and
   `amb-009` carry `locale: es-ES`, the interpreter reads the Spanish, and the
   deterministic reply comes back in English. Every fact is right; the register
   is wrong for the stated audience. Labelled `tone: 1` on all three. The
   composer is the one component the locale does not yet reach, and that is now
   a known gap with a number attached rather than a surprise.
1. **The degradation replies repeat themselves.** `deg-001` ends "Nothing has
   been submitted — please try again shortly. Nothing has been submitted." and
   `deg-002` states the unverified conflict check twice. Honesty anchors were
   met (labelled 3); the padding is a composer defect the tone rubric would have
   caught had those scenarios carried it.

## Known limits

- **The judge has never run in this repository.** No credential ships with it, so
  every judged scenario currently reports `skipped:no-credential`. The machinery
  around the judge — prompt assembly, strict parsing, every rejection path, the
  transcript, the κ arithmetic — is exercised on every push against hand-written
  replies. The model itself has not answered. Recorded as D-9 in
  [`DEVIATIONS.md`](DEVIATIONS.md), closing on the first keyed nightly run.
- **One labeller is not inter-rater reliability.** κ between one human and one judge
  measures agreement with *that human*. A second labeller on the same scenarios
  would measure whether the anchors are legible to anyone but their author, and that
  is the more interesting number. It is not required by the gate above because
  requiring two people would stop calibration happening at all in a one-person
  repository — an honest compromise, named here rather than glossed.
- **Labels age.** A label is against a scenario, a fixture and a spec version. When
  `SPEC.md`'s version moves, existing labels describe a slightly different question.
  The harness does not currently expire them; the first time that matters, the label
  format gains a `specVersion` field and stale labels stop counting.
