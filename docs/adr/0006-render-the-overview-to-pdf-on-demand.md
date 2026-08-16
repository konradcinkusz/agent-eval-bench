# ADR-0006: Render the project overview to PDF via LaTeX, built on demand

- **Status**: Accepted
- **Date**: 2026-08-15
- **Phase**: Post-launch documentation — added after Phase 9 shipped; not part
  of the original ten-phase plan in `README.md`.
- **Relates to**: P14 (documentation lives in the repository and records
  reasoning), architecture-standards' `docs/research/00-RESEARCH-DOCUMENTATION.md`
  (the house LaTeX preamble and its "PDFs are build output" rule),
  [ADR-0001](0001-record-architecture-decisions.md)

## Context

By Phase 9 this repository's documentation was genuinely comprehensive —
`SPEC.md`, `FINDINGS.md`, `PRODUCTION.md`, `DEVIATIONS.md`, `COMPLIANCE.md`,
`CALIBRATION.md`, five ADRs, and the README itself — but comprehensive across
eight separate files, on GitHub, for a reader willing to navigate between them.
That is the right shape for a contributor. It is the wrong shape for a reader
who wants the whole project in one sitting without opening a repository at
all — someone handed a link and half an hour, rather than a clone and an
afternoon.

`architecture-standards` already prescribes a house LaTeX style for exactly
this need — "when a result needs to travel outside the repository... it
graduates to a LaTeX paper" — complete with a shared preamble (colors,
`titlesec` section styling, `fancyhdr`) so a paper from any repository in the
estate looks like it came from the same shop. A sibling repository,
`copilot-scope`, already has a working CI workflow that builds such a paper to
PDF on demand. Neither of those was written with a document like this one in
mind, though: the standard's LaTeX guidance is explicitly for a *paper
derived from a research study* — one that asks a question the repository
didn't already know the answer to. A synthesizing overview of an already-built
system is not that, and pretending otherwise would misuse a term the standard
defines narrowly on purpose.

## Decision

Add `docs/OVERVIEW.md` as the single synthesizing document — collecting the
integration target, architecture, spec-first workflow, evaluation methodology,
findings and values already recorded elsewhere into one linear read — and a
LaTeX presentation of it at `docs/papers/agent-eval-bench-overview.tex`,
reusing the house preamble verbatim for visual consistency with the rest of
the author's formal documents. A new `workflow_dispatch`-only GitHub Action,
`build-overview-pdf.yml`, builds it to a downloadable run artifact using the
same `xu-cheng/latex-action` step `copilot-scope`'s own PDF workflow already
uses. `docs/papers/` is a new, honestly-named sibling to
`docs/research/papers/` — not a claim that this document is a research paper
under the standard that path belongs to.

## Alternatives considered

### Pandoc, straight from `docs/OVERVIEW.md`, no LaTeX intermediate

**Why it is attractive:** one fewer file to keep in sync, and no manual
prose-to-LaTeX transcription to maintain by hand.

**Why it lost:** the estate already committed to a specific house look for
exactly this purpose — colors, fonts, section styling — and Pandoc's default
LaTeX template does not produce that look without writing essentially the same
custom template Pandoc would need anyway. At that point, using the template
that already exists and already matches the rest of the author's formal
documents is strictly less work, not more.

### Commit a pre-built PDF instead of generating one on demand

**Why it is attractive:** no CI dependency, and it works even if the LaTeX
action breaks upstream.

**Why it lost:** it directly violates "PDFs are build output," and a
committed PDF drifts from `docs/OVERVIEW.md` the first time the Markdown is
edited and nobody remembers to rebuild it — the same drift class `F-7`
(`FINDINGS.md`) already caught once in this repository, in a different file.

### Tag-driven trigger, mirroring `flyio.yml` or `copilot-scope`'s own workflow

**Why it is attractive:** consistency with the one existing precedent in the
estate, and a PDF would ship automatically alongside every release.

**Why it lost:** this repository's documentation has no release cadence of
its own. `SPEC.md`'s version tracks the agent's behaviour contract, not the
documentation, and tying a documentation PDF to a `v*` tag would mean either
tagging releases the agent's own versioning has no reason to make, or building
a stale PDF against whatever the last tag happened to be. `workflow_dispatch`
matches how the document is actually used: on demand, when someone wants the
current file.

### File it as a study under `docs/research/`

**Why it is attractive:** reuses an existing, CI-enforced convention exactly,
`.gitignore` coverage included.

**Why it lost:** `00-RESEARCH-DOCUMENTATION.md` is explicit about scope — "a
study asks a question whose answer wasn't known before the work was done" —
and this document answers no such question; it synthesizes what `SPEC.md`,
`FINDINGS.md` and the rest already established. Filing it as a study would
misuse a term the standard defines narrowly on purpose.

## Consequences

**What this makes easy:**

- A reader outside GitHub gets one document instead of eight.
- Regenerating the PDF costs one click and no local LaTeX install.
- The visual result matches the author's other formal documents without
  writing a new template.

**What this makes hard:**

- `docs/OVERVIEW.md` and `docs/papers/agent-eval-bench-overview.tex` are two
  files that can drift. The `.tex` is a curated presentation, not a mechanical
  transform of the Markdown, so nothing enforces that a later edit to
  `OVERVIEW.md` is reflected in the paper — unlike `prompts/` and `agents/`
  against `SPEC.md`, there is no CI change-coupling check here.

**What we accept:**

- The workflow has no test proving it produces a correct PDF beyond someone
  actually running it — nothing triggers it automatically, so a break in
  `xu-cheng/latex-action` or a future TeX Live change is caught only the next
  time a human clicks **Run workflow**, not by CI on every push.

## Revisit when

- `docs/OVERVIEW.md` and the `.tex` presentation visibly diverge more than
  once — add a lightweight CI check (a section-heading diff, say) rather than
  a full change-coupling rule.
- This documentation acquires an actual release cadence of its own, at which
  point the tag-driven alternative should be reconsidered.
