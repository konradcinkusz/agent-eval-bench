<!--
  Reviews without repro and impact are the reason this template exists
  (REPO-BASELINE.md §1). Delete the sections that genuinely do not apply — but
  delete them, do not leave them blank: a blank section reads as "not checked".
-->

## What changed

<!-- One paragraph. What behaviour is different after this PR than before it? -->

## Why

<!--
  The reasoning, not the steps (P14). If this PR argues *against* something —
  a rejected alternative, a deviation from the standards — say so here and link
  the ADR that records it.
-->

## Spec impact

<!--
  Spec before code, always. AI-EVALS.md §2 makes the spec the thing an edit is
  reviewed against; if implementation revealed the spec was wrong, the spec is
  amended in THIS PR and the amendment is described here.
-->

- [ ] No behaviour change — `docs/SPEC.md` untouched
- [ ] Behaviour change, and `docs/SPEC.md` is amended in this PR
- [ ] New hard constraint added (and it has a scenario that fails without it)

## Eval impact

<!--
  A prompt edit is a behaviour change with no code diff. Change detection treats
  `prompts/`, `agents/` and `evals/` as eval-triggering paths, so a change to any
  of them re-runs the suite. State what the run showed.
-->

- [ ] Constraint scenarios: 100% pass (hard gate — no exceptions, no "flaky")
- [ ] Behaviour scenarios: pass rate at or above the recorded baseline
- [ ] Judge criteria: at or above threshold, or the movement is explained below
- [ ] Baseline re-recorded, and the diff is in this PR for review

**Scenario / criteria diff vs baseline:**

<!-- Paste the eval job's diff, or write "no eval-triggering paths touched". -->

## Verification

<!--
  What did you actually run, and what did it print? "Tests pass" is not evidence;
  the output is. A scenario that executed nothing FAILS — it does not skip
  (E2E-ACCEPTANCE-TESTING.md §2).
-->

- [ ] `dotnet build` clean (warnings are errors in this repo)
- [ ] `dotnet test` green
- [ ] Eval suite run locally, or explicitly not applicable
- [ ] Secret scan clean (`scripts/scan-secrets.sh`, the local mirror of the CI job)
- [ ] Fresh-clone check still true: `git clone && dotnet run` works with **zero** credentials

## Standards conformance

<!--
  This repository is the estate's worked example, so it is held to the constitution
  it demonstrates. Where it must deviate, the deviation is recorded — dated and
  reasoned — in `docs/DEVIATIONS.md`, and the standard amendment is proposed.
-->

- [ ] No new deviation from `00-REFERENCE-ARCHITECTURE.md`
- [ ] New deviation, recorded in `docs/DEVIATIONS.md` with a date and a reason
- [ ] Amendment proposed back to `architecture-standards`

## Public-repo checks

<!-- This repository is public. Every commit is disclosed the moment it is pushed. -->

- [ ] No secrets, tokens, or credentials — including in comments and test fixtures
- [ ] No real personal data; fixtures are fictional
- [ ] No client or customer names
- [ ] English only
