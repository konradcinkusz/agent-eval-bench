# Baselines

The recorded pass state a regression is measured against.

`layer1.json` says what the deterministic Layer 1 suite did the last time it was
recorded. It exists so a run can tell a **regression** from a **Tuesday** — a
scenario that fails today and passed at the baseline is a finding; one that fails
today and failed at the baseline is a known gap somebody wrote down.

## What it does not do

**It does not soften the constraint gate.** Constraint-gated scenarios must pass at
100% on every run, whatever this file says. A baseline that recorded a constraint
as failing would be a merged violation with a signature next to it, so the harness
never consults it for those.

## When it must be re-recorded

- **The spec version moved.** The harness refuses to compare across versions
  (`docs/SPEC.md` §8.4): a baseline records a pass rate against a specific contract
  and a specific world, and comparing across that boundary is a measuring stick that
  changed length between readings. A failing
  `The_baseline_was_recorded_against_this_version_of_the_contract` is that rule
  firing, not a flake.
- **A fixture was edited.** Editing `evals/fixtures/*.yaml` changes what the
  baseline measured without changing a single scenario file. That is a suite version
  bump, and the bump is what the check above catches.
- **A scenario was added or removed.** Both halves are checked: an unrecorded
  scenario is one the baseline cannot measure, and a stale entry is a scenario
  somebody deleted without saying so.
- **A scenario started passing.** Not a failure — blocking a merge for getting
  better is how a baseline becomes a ceiling — but the run prints it, and an
  unrecorded improvement means this file no longer describes the suite.

## What the interpreter field is for

A baseline gathered with the rule-based interpreter does not describe a run against
a model, and merging them would produce one number describing neither
([ADR-0004](../../docs/adr/0004-pin-the-model-and-never-fall-back-silently.md)).
The harness refuses to compare across that boundary too.

Re-record in the **same pull request** as the change that moved it, so the diff is
reviewed alongside its cause rather than as a mysterious follow-up.
