# ADR-0002: Mock-first — the demonstrated path runs with zero credentials

- **Status**: Accepted
- **Date**: 2026-08-14
- **Phase**: 0 — repository baseline (the decision constrains every later phase)
- **Relates to**: P8 (optional dependencies degrade), P11 (anti-corruption at the edge), AI-EVALS.md §4, §9

## Context

This repository has two audiences and they want opposite things. A reviewer wants
to clone it and see it work, in one command, on a machine with no accounts and no
API keys. An integration wants to prove the agent really talks to a real workforce
system over a real Model Context Protocol server, with real permissions enforced
by a real identity.

Only one of those can be the default, and the choice is not a matter of taste
here — it decides whether the eval suite means anything:

- **Evals must be reproducible.** A suite whose results move because a remote
  system's fixture data changed cannot distinguish a regression from a Tuesday.
  AI-EVALS.md §9 names this directly: *"eval results differ run to run with no
  change — nondeterminism unpinned … or fixture state not reset between
  scenarios."*
- **The gate must run on every prompt edit.** §6 makes the constraint layer a
  100% hard block on every pull request touching prompts, tools, model or agent
  definition. A gate that needs a credential is a gate that is skipped on forks,
  skipped when the credential rotates, and eventually removed.
- **P8's test is literal.** *"`git clone && dotnet run` with zero cloud
  credentials must produce a working system with reduced features."* This
  repository is the estate's worked example; failing its own constitution's
  smoke test would be a strange way to demonstrate the standard.
- **The live path carries a real identity.** The MCP server acts as the
  authenticated user and enforces that user's permissions on every call. Making
  it the default would mean the demo either ships a credential or does not run.

## Decision

The mock workforce tools are the product. `WorkforceTools:Mode` selects between
`Mock` (default) and `Mcp`, both implementing one internal `IWorkforceTools`
interface, with the external dialect normalised at the boundary (P11) so nothing
downstream knows which is active.

Concretely, and enforced rather than intended:

1. A fresh clone with an empty `.env` runs the agent, the showcase page, and the
   full Layer-1 eval suite, green. No credential is required for any of it.
1. LLM non-determinism is contained the same way: `Llm:Provider=Replay` is the
   default, replaying recorded model responses. A live provider is opt-in.
1. Every optional integration degrades with a working fallback and a log line
   naming the variable that was absent — never a startup failure.
1. **An absent credential produces an explicit `SKIPPED` with a reason, never a
   pass.** This applies most sharply to the Layer-2 judge job: with no key it
   reports skipped in CI, because a judge that shows green without having run is
   the most dangerous state an eval suite can be in.
1. The live MCP mode is never enabled on the public deployment. That deployment
   does not carry the credentials at all, so the mode is structurally
   unavailable rather than merely switched off.

## Alternatives considered

### Live MCP as the default, mock as a fallback

**Why it is attractive:** it is the more impressive demo, and it proves the
integration works rather than asserting it. The whole point of an integrations
role is integrating.

**Why it lost:** it makes the eval suite depend on a remote system's state, which
breaks the property the repository exists to demonstrate. It also means a stranger
cannot run the demo — and the deliverable is a thing a stranger can run and judge.
The integration is still proved, as a documented, manually exercised path against
a demo environment; it is simply not what CI depends on.

### Record/replay against the live server (VCR-style cassettes) as the only fixture source

**Why it is attractive:** fixtures that are real by construction, with no
hand-written mock drifting away from the actual API dialect.

**Why it lost:** cassettes recorded from a live workforce system contain real
employee names, real leave balances and real identifiers. This repository is
public, and no amount of scrubbing makes that a decision I want to defend. The
mock is seeded with fictional data instead, and drift from the real dialect is
handled where it belongs — in the anti-corruption layer, exercised by the live
mode when it is run manually.

### A shared hosted demo backend that the eval suite talks to

**Why it is attractive:** one fixture set, identical for every contributor and for
CI, without shipping mock code.

**Why it lost:** it is a service to operate, a cost to carry, and a single point
of failure for the gate — and it still does not make a fresh clone work offline.
It converts a zero-dependency property into an availability problem.

## Consequences

**What this makes easy:** a reviewer with nothing installed but the .NET SDK gets
the full experience; the constraint gate runs on every pull request including from
forks; eval results are reproducible, so a diff against the baseline means what it
says; the public deployment costs approximately nothing per visitor.

**What this makes hard:** the mock can drift from the real MCP dialect without
anyone noticing, because nothing in CI talks to the real server. That is a real
risk and it is not fully mitigated — the mitigation is a manually exercised live
mode plus a thin anti-corruption layer that keeps the surface area small enough to
re-verify by reading.

**What we accept:** CI never proves the live integration works. The claim
"this agent works against a real MCP server" is therefore supported by a
documented manual run, not by a green check. Stating that plainly is the point;
an unacknowledged gap is drift.

## Revisit when

A demo-environment credential exists that can be safely scoped to a read-only,
fictional dataset and held as a repository secret. At that point a nightly (not
per-pull-request) job could exercise the live path and turn the accepted gap above
into a measured one — without moving the per-pull-request gate off the mock.
