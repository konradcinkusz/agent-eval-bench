# docs/diagrams/

The Mermaid source for every diagram in [`../DIAGRAMS.md`](../DIAGRAMS.md), one
file each.

**Read the diagrams in [`../DIAGRAMS.md`](../DIAGRAMS.md)** — it renders them,
and each one comes with the paragraph that says what to look at. This directory
exists so a diagram can be used on its own: dropped into a slide, pasted into an
issue, rendered to SVG, or diffed in isolation.

Both copies are kept identical by [`../../scripts/check-diagrams.mjs`](../../scripts/check-diagrams.mjs),
which runs in CI and fails the build if they drift. The pairing rule is the
section id: `### A1.` in `DIAGRAMS.md` owns `a1-*.mmd` here.

```bash
npm run lint:diagrams                          # verify every pair
npx -y @mermaid-js/mermaid-cli -i a1-system-context.mmd -o a1.svg   # render one
```

## Index

### Context and architecture

| Id | File | Diagram |
|---|---|---|
| `A1` | [`a1-system-context.mmd`](a1-system-context.mmd) | System context — who talks to what |
| `A2` | [`a2-solution-layout.mmd`](a2-solution-layout.mmd) | Solution layout — five projects, and what ships |
| `A3` | [`a3-turn-components.mmd`](a3-turn-components.mmd) | One turn, end to end — components |
| `A4` | [`a4-step-pipeline.mmd`](a4-step-pipeline.mmd) | The step pipeline — eleven steps, in registration order |
| `A5` | [`a5-tool-boundary.mmd`](a5-tool-boundary.mmd) | The tool boundary — the decorator chain |
| `A6` | [`a6-token-state-machine.mmd`](a6-token-state-machine.mmd) | The confirmation token — a state machine |

### User flows

| Id | File | Diagram |
|---|---|---|
| `B1` | [`b1-flow-reference-path.mmd`](b1-flow-reference-path.mmd) | The reference path — sick today and probably tomorrow |
| `B2` | [`b2-flow-user-rejects.mmd`](b2-flow-user-rejects.mmd) | The user says no |
| `B3` | [`b3-flow-refusal.mmd`](b3-flow-refusal.mmd) | A refusal — "approve Sam's holiday for me" |
| `B4` | [`b4-flow-social-engineering.mmd`](b4-flow-social-engineering.mmd) | Social engineering — "my manager already approved it" |
| `B5` | [`b5-flow-indeterminate-write.mmd`](b5-flow-indeterminate-write.mmd) | Degradation — a write whose fate is unknown |
| `B6` | [`b6-flow-mcp-session.mmd`](b6-flow-mcp-session.mmd) | The MCP session — one live connection |

### The eval bench

| Id | File | Diagram |
|---|---|---|
| `C1` | [`c1-eval-loop.mmd`](c1-eval-loop.mmd) | The measuring loop — the general picture |
| `C2` | [`c2-layer1-assertions.mmd`](c2-layer1-assertions.mmd) | Layer 1 — what a deterministic assertion actually reads |
| `C3` | [`c3-layer2-judge.mmd`](c3-layer2-judge.mmd) | Layer 2 — the judge, and why it is pinned |
| `C4` | [`c4-mutation-pass.mmd`](c4-mutation-pass.mmd) | The mutation pass — who checks the instrument |
| `C5` | [`c5-production-to-scenario.mmd`](c5-production-to-scenario.mmd) | From a production failure to a new scenario |
| `C6` | [`c6-both-layers.mmd`](c6-both-layers.mmd) | Both layers on one page — everything the trace is graded by |

### Infrastructure and delivery

| Id | File | Diagram |
|---|---|---|
| `D1` | [`d1-deployment-topology.mmd`](d1-deployment-topology.mmd) | Deployment topology |
| `D2` | [`d2-ci-gates.mmd`](d2-ci-gates.mmd) | The CI gates — what must be true before anything merges |
| `D3` | [`d3-tag-driven-deploy.mmd`](d3-tag-driven-deploy.mmd) | Deploying is pushing a tag |
| `D4` | [`d4-eval-triggering-paths.mmd`](d4-eval-triggering-paths.mmd) | Why `prompts/` and `agents/` are eval-triggering paths |
