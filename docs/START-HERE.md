# Start here

The front door to this repository's documentation.

There is a lot of it, and it is not all the same kind of thing. This page tells
you which kind you need and sends you there. If you read one page before any
other, read this one.

> **Polish version:** [`START-HERE.pl.md`](START-HERE.pl.md)

## What is this repository?

**An instrument for measuring whether an AI agent still behaves the way you
specified it to** — after a code change, a prompt edit, a model swap, or no
change at all.

Inside it there is an HR agent that books time off. **That agent is the
specimen, not the product.** It was chosen because it concentrates every hard
property at once: an irreversible write, date arithmetic across timezones and
holidays, permission rules, a hostile-input surface, and an obvious need for a
human to say yes. The repository's own one-line summary:

> The agent is the excuse. **The eval bench is the deliverable.**

## Four kinds of document, and which one you want

This documentation follows [Diátaxis](https://diataxis.fr/), which observes that
documentation serves four distinct needs and that a page trying to serve two of
them serves neither well. The four are split by two questions: *are you working
or studying?* and *do you need action or knowledge?*

| | **Practical** — action | **Theoretical** — knowledge |
|---|---|---|
| **Studying** (acquiring skill) | 📘 **Tutorials** — lessons that take you through doing something for the first time | 💡 **Explanation** — background, context, and why things are the way they are |
| **Working** (applying skill) | 🔧 **How-to guides** — recipes for a task you already understand | 📇 **Reference** — dry, exhaustive description of the machinery |

Pick the row by what you are doing right now, not by how much you know.

### 📘 Tutorials — "I have never run this before"

Learning-oriented. You follow along, everything works, and you finish having
seen the thing with your own eyes. No decisions to make, no theory.

1. [**Your first run**](tutorials/01-first-run.md) — clone it, start it, type a
   sentence, and watch the agent do all the work and then stop. About 15
   minutes, no credentials, no accounts.
1. [**Your first scenario**](tutorials/02-your-first-scenario.md) — write a
   scenario that fails, then make it pass. This is the loop the whole repository
   exists to support. About 25 minutes.

### 🔧 How-to guides — "I know what I want; how do I do it?"

Task-oriented. Each one assumes you already understand the surrounding ideas and
gets straight to the steps.

- [Run the evals](how-to/run-the-evals.md) — the whole suite, one layer, or one
  scenario
- [Add a scenario](how-to/add-a-scenario.md) — including the rules the validator
  enforces
- [Add a behaviour](how-to/add-a-behaviour.md) — spec first, then scenario, then
  a pipeline step
- [Debug a failing scenario](how-to/debug-a-failing-scenario.md) — reading the
  trace when an assertion goes red
- [Enable the judge](how-to/enable-the-judge.md) — turning Layer 2 from
  `skipped:no-credential` into a real run

### 📇 Reference — "what exactly is the name of that thing?"

Information-oriented. Look things up; do not read start to finish.

| Document | What it describes |
|---|---|
| [`SPEC.md` §2](SPEC.md) | The vocabulary: tools, trace events, turn outcomes, identifiers |
| [`SPEC.md` §3–§4](SPEC.md) | The 16 behaviours and the 7 hard constraints, by id |
| [`SPEC.md` §6](SPEC.md) | The 7 refusals, and how each one must look |
| [`dokumentacja.pl.html` §5](dokumentacja.pl.html) 🇵🇱 | Complete trace vocabulary — every event, attribute and closed value set |
| [`dokumentacja.pl.html` §16–§18](dokumentacja.pl.html) 🇵🇱 | Configuration keys with defaults, the HTTP surface, every spend ceiling |
| [`evals/schema/scenario.schema.json`](../evals/schema/scenario.schema.json) | The scenario file format, enforced |
| [`evals/rubrics/judge.yaml`](../evals/rubrics/judge.yaml) | The five rubrics, their scales, thresholds and anchors |
| [`flyio/SECRETS.md`](../flyio/SECRETS.md) | Every secret, and what degrades without it |

### 💡 Explanation — "why is it built this way?"

Understanding-oriented. Read these when you want the reasoning rather than the
steps.

| Document | What it explains |
|---|---|
| [`DIAGRAMS.md`](DIAGRAMS.md) | The whole system as 22 diagrams — architecture, user flows, the eval loop, delivery |
| [`dokumentacja.pl.html`](dokumentacja.pl.html) 🇵🇱 | The complete technical documentation, 26 sections in 7 parts |
| [`index.html`](index.html) / [`index.pl.html`](index.pl.html) 🇵🇱 | The one-pagers — the argument, for a first-time reader |
| [`FINDINGS.md`](FINDINGS.md) | What the suite actually caught: fourteen defects, seven of them in the instrument |
| [`CALIBRATION.md`](CALIBRATION.md) | Why a judge must agree with a human before it may block anything |
| [`PRODUCTION.md`](PRODUCTION.md) | What changes when this runs somewhere real, and what silently stops working |
| [`DEVIATIONS.md`](DEVIATIONS.md) | Where this repository knowingly departs from the standards it is measured against |
| [`adr/`](adr/README.md) | Five architecture decisions, each with the alternatives that were rejected |

## The one idea worth having before anything else

An **eval** is not a test in CI. It is a measurement of system behaviour against
a specification, and the CI gate is only one of four places you consume it:

| When | What for | Classic analogue |
|---|---|---|
| In the development loop | Iterate a prompt and watch the pass rate move — evals as a *design* tool | red-green-refactor |
| On a change | Regression against a recorded baseline | regression tests |
| Choosing a model | The same set against different models, decided on numbers | benchmark |
| Continuously in production | Real sessions scored by the same apparatus | monitoring / SLO |

And the trigger for the second row is **not** "a code change". In a system with a
language model, behaviour is shaped by the code, the system prompt, the tool
descriptions, the agent definition, the model version and its parameters, and
any retrieval data — and half of those are not code at all. That is why
`prompts/` and `agents/` are eval-triggering paths here, and why a CI check fails
a pull request that edits them without moving the spec.

The shortest version:

> Tests answer *"does the code do what I wrote?"*
> Evals answer *"does the system do what I specified?"* — whichever moving part
> changed, and whether or not anything changed at all.

## Conventions across all of it

- **Names are real.** Class names, step names, trace event names and workflow
  filenames in any document are copied from the source, so they can be grepped.
  Where a document and the code disagree, the code is right and the document is a
  bug (`REPO-BASELINE.md` §8).
- **Counts live in one place.** Scenario and assertion totals are recomputed in
  [`FINDINGS.md`](FINDINGS.md) and are not repeated elsewhere, because a number
  copied into prose is a number that goes stale on the next commit.
- **Unflattering things are written down.** [`DEVIATIONS.md`](DEVIATIONS.md)
  exists so a reader never has to infer a gap. Two worth knowing before you
  judge anything: the MCP adapter has never run against a live server, and the
  production loop is plumbed but has never carried real traffic.
- **Bilingual pages carry a `.pl` twin.** Tutorials, how-to guides and this page
  exist in English and Polish; a CI check
  ([`scripts/check-doc-parity.mjs`](../scripts/check-doc-parity.mjs)) fails if one
  half is edited without the other. Everything else — the spec, the scenarios,
  the agent definition, the source — is English only.
