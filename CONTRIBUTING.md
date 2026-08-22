# Contributing

Thank you for considering it. This repository is unusual in one way that shapes
every contribution: the specification and the scenarios are the deliverable, and
the agent exists to be measured. A change that makes the agent nicer and the
measurement weaker is a regression here, whatever it looks like elsewhere.

## The ground rules, stated once

1. **Spec before code.** A behaviour change starts in
   [`docs/SPEC.md`](docs/SPEC.md) and lands with its scenarios in the same pull
   request. CI enforces this mechanically: a change to `prompts/` or `agents/`
   without a change to the spec fails the `coupling` job, because a prompt edit
   is a behaviour change with no code diff.
1. **The dataset is data.** Scenarios are YAML under
   [`evals/scenarios/`](evals/README.md), validated against a
   [strict schema](evals/schema/scenario.schema.json) on every push. A new
   scenario needs no C# — [`evals/README.md`](evals/README.md) is the tour, and
   the [scenario issue template](.github/ISSUE_TEMPLATE/eval_scenario.yml) asks
   for exactly what a scenario needs.
1. **A changed measuring stick is versioned.** Editing a fixture or a rubric
   changes what the baseline measured; CI requires the suite version in
   [`agents/absence-concierge/definition.json`](agents/absence-concierge/definition.json)
   to move with it, and the baseline to be re-recorded in the same pull request.
1. **Zero credentials, always.** Everything a contributor needs runs green
   offline: the build, the unit tests, the Layer 1 suite, the lint. If your
   change only works with a key, it degrades without one — explicitly, with a
   reason, never a silent pass ([ADR-0002](docs/adr/0002-mock-first-zero-credential-default.md)).
1. **No real personal data.** Every fixture is fictional. The issue templates
   ask you to confirm it; review will too.

## Before you open a pull request

The local loop mirrors CI, and `scripts/setup.sh` wires the hooks that run the
fast half of it on commit:

```bash
./scripts/setup.sh        # prerequisites, hooks, .env — a minute
dotnet build              # warnings are errors in this repository
dotnet test               # unit tests, trace contract, and the Layer 1 evals
npm install && npm run lint   # docs, links, diagrams, parity, every scenario
./scripts/scan-secrets.sh # the local mirror of the CI secret scan
```

The [pull request template](.github/pull_request_template.md) is not ceremony —
each section exists because a review without it has already gone wrong once.
Delete the sections that genuinely do not apply; do not leave them blank.

## What makes a good contribution here

- **A scenario that catches something.** The best contribution to an eval bench
  is a behaviour it cannot yet see. Denied and adversarial scenarios must assert
  the refusal happened *and* the call did not — the schema enforces the absence
  assertion.
- **A defect in the instrument.** Seven of the twelve defects in
  [`docs/FINDINGS.md`](docs/FINDINGS.md) were in the measuring apparatus or the
  spec, not the agent. Finding one is not embarrassing; it is the point.
- **A deviation, written down.** Where the repository must depart from
  [the standards it follows](https://github.com/konradcinkusz/architecture-standards),
  the departure is recorded in [`docs/DEVIATIONS.md`](docs/DEVIATIONS.md) —
  dated, reasoned, with a closing condition — never silently.

## Scope, so a rejection is predictable

The [non-goals in the README](README.md#non-goals) are real boundaries: one
page of frontend, one agent, no payments or identity, no fork of the standards.
A pull request that crosses one will be declined with a pointer here rather
than reviewed as if the boundary did not exist — open an issue first if you
believe the boundary is wrong, and argue with the boundary, not around it.

## Reporting problems

- Bugs: the [bug template](.github/ISSUE_TEMPLATE/bug_report.yml) asks for the
  trace, because the trace is what gets graded.
- Security: see [`SECURITY.md`](SECURITY.md) — privately, please.
