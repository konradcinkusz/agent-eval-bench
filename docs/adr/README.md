# Architecture Decision Records

Decisions that shape this repository, each recorded when it was made, with the
alternatives that lost and why.

P14: *"A document that says 'we considered X and rejected it because Y' is worth
more than a document that lists commands."* These files are that document.

## What belongs here, and what does not

This repository does **not** re-derive the estate's architecture. The fifteen
principles, the deployment model, the testing strategy and the eval standard live
in [`architecture-standards`](https://github.com/konradcinkusz/architecture-standards)
and are read, not restated. So:

| Kind of decision | Where it goes |
|---|---|
| Specific to this repository — which MCP auth path, how LLM responses are replayed, how the confirmation gate is represented in a trace | An ADR here |
| A departure from the estate standards | [`../DEVIATIONS.md`](../DEVIATIONS.md), in the §3a format: dated, reasoned, with a proposed amendment |
| A restatement of something the standards already say | Nowhere. Link to the standard instead |

A decision recorded in two places drifts in one of them.

## Index

| # | Title | Status | Date |
|---|---|---|---|
| [0001](0001-record-architecture-decisions.md) | Record architecture decisions in this repository | Accepted | 2026-08-14 |
| [0002](0002-mock-first-zero-credential-default.md) | Mock-first: the demonstrated path runs with zero credentials | Accepted | 2026-08-14 |
| [0003](0003-agent-decisions-are-trace-attributes.md) | The agent's decision is a trace attribute, not prose | Accepted | 2026-08-14 |
| [0004](0004-pin-the-model-and-never-fall-back-silently.md) | Pin the agent model and the judge model separately, and never fall back silently | Accepted | 2026-08-15 |
| [0005](0005-the-mcp-sdk-lives-behind-a-one-method-session.md) | The Model Context Protocol SDK lives behind a one-method session | Accepted | 2026-08-15 |
| [0006](0006-render-the-overview-to-pdf-on-demand.md) | Render the project overview to PDF via LaTeX, built on demand | Accepted | 2026-08-15 |

Later phases add their own. The decisions already settled and awaiting the phase
that implements them — the public-demo budget ceiling among them — are listed in
the README's phase plan and get an ADR when they are built, not before.

## Format

Copy [`0000-template.md`](0000-template.md). Number sequentially; never renumber.

Statuses: **Proposed** → **Accepted** → **Superseded by ADR-NNNN** / **Deprecated**.
A superseded ADR is never deleted or edited into agreement with its successor —
the record of what was believed at the time is the point.
