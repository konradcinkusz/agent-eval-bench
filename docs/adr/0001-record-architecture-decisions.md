# ADR-0001: Record architecture decisions in this repository

- **Status**: Accepted
- **Date**: 2026-08-14
- **Phase**: 0 — repository baseline
- **Relates to**: P14 (documentation lives in the repository and records reasoning)

## Context

This repository is built as the estate's canonical worked example of
[`AI-EVALS.md`](https://github.com/konradcinkusz/architecture-standards/blob/main/docs/guides/AI-EVALS.md).
Its value to a reader is not that it works — plenty of demos work — but that every
non-obvious choice in it can be traced to a reason. That is only true if the
reasons are written down at the moment they are chosen, when the alternatives are
still live and the constraint that killed one of them is still remembered.

There is a second, sharper pressure specific to an agent project. Several
decisions here are of the kind that look arbitrary six months later and are then
quietly reversed: how model non-determinism is contained, why the judge model is
pinned, why the confirmation gate is a trace event rather than a return value, why
the live integration is the *optional* path. Each of those has a failure mode
behind it. Without the record, the reversal looks like a simplification.

The estate already has the habit and the evidence for it: `copilot-scope`'s
`STRATEGY.md` was written when that repo had no users, and
the reference SaaS's `INFRASTRUCTURE-ANALYSIS.md` argues *against* a change on
switching-cost grounds, with numbers. Both are cited in P14 as worth more than
documents that list commands.

## Decision

We keep Architecture Decision Records in `docs/adr/`, numbered sequentially, one
file per decision, in the format of [`0000-template.md`](0000-template.md). An ADR
is written in the same pull request as the change it describes.

The scope is deliberately narrow: an ADR records what is **specific to this
repository**. Anything the estate standards already settle is linked, never
restated. Anything where this repository must *depart* from those standards is not
an ADR at all — it goes in [`../DEVIATIONS.md`](../DEVIATIONS.md) in the
constitution's §3a format, dated and reasoned, with the corresponding amendment
proposed back to `architecture-standards`.

## Alternatives considered

### A single `DECISIONS.md` appended over time

**Why it is attractive:** one file, no numbering scheme, no index to maintain, and
the whole history reads top to bottom.

**Why it lost:** a single file has no per-decision status. When decision 4
supersedes decision 2, the file either grows a contradiction or gets edited so
that decision 2 never appears to have been made — and the second is worse, because
the record of what was believed at the time is exactly what a future reader needs.
Numbered files carry `Superseded by` in their own front matter without anyone
rewriting history.

### Decisions in commit messages and pull request descriptions

**Why it is attractive:** zero additional files, and the reasoning sits adjacent to
the diff that implements it.

**Why it lost:** it is not findable. Nobody greps a year of pull request bodies to
learn why the judge model is pinned; they read the file that is obviously about
that, or they conclude there is no reason. P14's point is precisely that the
reasoning must live *in the repository*, in a place a reader will look.

### No ADRs — rely on the standards repository

**Why it is attractive:** the estate constitution is already thorough, and this
repository's whole premise is that the architecture must not be re-derived.

**Why it lost:** the standards are repo-agnostic by construction. They cannot
record which MCP authentication path this repository settled on after finding
OAuth Dynamic Client Registration awkward from a headless client, because that is
not a general rule — it is this repository's finding. Deleting the local record
does not move the knowledge into the standards; it deletes it.

## Consequences

**What this makes easy:** reviewing a behaviour change against its stated reason;
onboarding a reader who wants the *why* rather than the *how*; proposing an
amendment back to the standards, because the local decision and its reasoning are
already written in a reviewable form.

**What this makes hard:** nothing structural, but it adds a file to some pull
requests. That cost is real and is the reason ADRs get abandoned in most
repositories that adopt them.

**What we accept:** the discipline decays if enforced only by memory. Two
mechanisms hold it up here: the pull request template has a "Why" section that asks
for the ADR link, and `CODEOWNERS` puts `docs/adr/` under explicit review. Neither
is automation, and both are honest about being process rather than enforcement.

## Revisit when

The index exceeds roughly thirty entries, at which point the flat list stops being
scannable and the ADRs need grouping by area rather than by number.
