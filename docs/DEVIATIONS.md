# Deviations from the reference architecture

Where this repository departs from
[`00-REFERENCE-ARCHITECTURE.md`](https://github.com/konradcinkusz/architecture-standards/blob/main/docs/architecture/00-REFERENCE-ARCHITECTURE.md)
and its guides — in the constitution's own §3a format: dated, reasoned, and with
the amendment it implies proposed back to the standard.

The rule this file exists to satisfy is the constitution's own:

> *"When one is fixed, delete the row. When a new one is accepted deliberately,
> add it with the reasoning — an acknowledged deviation is a decision; an
> unacknowledged one is drift."*

And its sharper half, from the same document: **a principle whose named violation
stays open indefinitely reads as optional.** Every row below therefore carries a
date and either a closing condition or an explicit acceptance.

This repository is the estate's worked example of the eval standard, so it is held
to the constitution more strictly than a product repo, not less. A short list here
is the goal; an empty list maintained by not looking is not.

---

## Open deviations

| # | Deviation | Principle / guide | Since | Reason | Closes when |
|---|---|---|---|---|---|
| D-1 | No CodeQL/SAST job in CI | REPO-BASELINE.md §1 | 2026-08-14 | There is no code to analyse. A CodeQL C# job on a repository with no solution fails at the autobuild step, and a permanently red required check trains everyone to ignore red checks. | Phase 2, in the same pull request that adds the first project. |
| D-2 | No `Dockerfile`, no `fly.toml`, no `.github/workflows-archive/` | P6, P7, REPO-BASELINE.md §5 | 2026-08-14 | Same reason: nothing to containerise, nothing to deploy, and no retired workflow to archive. `.dockerignore` is already committed because it must exist *before* the first image build, not after. | Phases 2 and 9 respectively. The archive directory appears the first time a workflow is retired, and never as an empty placeholder. |
| D-3 | Operational scripts are POSIX shell, not PowerShell | REPO-BASELINE.md §4 | 2026-08-14 | The guide mandates one setup script per repository, not a language. This repository is public, its CI runs on Ubuntu, and its readers are asked to judge it without installing anything — `bash` is what they already have. Recorded so the inconsistency with the rest of the estate is visible and deliberate. | Not planned. Accepted permanently for this repository. |
| D-4 | The secret-scanner container image is pinned to `:latest` | P12 (pinned, reproducible CI) | 2026-08-14 | A detection ruleset is worth what its newest rule catches. A pinned scanner silently stops detecting whatever upstream added last month, and the failure is invisible. The trade — a tool that can change under us — buys detection that does not decay. Everything else in CI is pinned. | Not planned. Accepted, and re-argued if a scanner upgrade ever breaks the job for an unrelated reason. |

---

## Extensions proposed back to the standards

Where this repository does something the standards do not yet describe, and the
standard should probably grow to cover it. These are not deviations — nothing is
being violated — but they are tracked here so the feedback loop is a list rather
than a memory.

| # | Extension | Target guide | Since | Status |
|---|---|---|---|---|
| E-1 | Amend the closing note of `AI-EVALS.md`, which currently says the first full worked example is *under construction*, to cite this repository. Until then that note is the standard's own §3a-style acknowledgement of the gap, and it stays accurate. | AI-EVALS.md, closing paragraph | 2026-08-14 | Blocked until this repository ships Phases 1–6. |
| E-2 | Extend the agent definition format with an MCP tool entry — `{"type": "mcp", "serverUrl": ...}` alongside the existing tool types — so an agent that reaches a Model Context Protocol server can be described as code like any other. | AZURE-AI-FOUNDRY-AGENTS.md §6 | 2026-08-14 | Proposed. Implemented here in Phase 2; amendment to be raised once the format has survived contact with a real provisioner. |
| E-3 | A worked calibration protocol for `AI-EVALS.md` §5, whose calibration rule is explicitly marked *"not yet demonstrated in the estate"*. This repository's `docs/CALIBRATION.md` is intended to become that demonstration — including, honestly, the state where no human labels exist yet. | AI-EVALS.md §5 | 2026-08-14 | Planned for Phase 5. |
| E-4 | **Define "write-classified".** The term is load-bearing in `AI-EVALS.md` §4 ("no write-classified span before a confirmation event") and is defined nowhere in the standards. A guide that gates on a term should say how the term is decided. This repository pins it as a normative per-tool table (`SPEC.md` §2.1) and derives the assertion from that table rather than from a naming convention, because a name-prefix rule silently classifies every future tool as a read. | AI-EVALS.md §4 | 2026-08-14 | Proposed. |
| E-5 | **A fixture pattern for eval scenarios.** `SERVICE-API-PATTERNS.md` §8 covers seeded *runtime* definitions ("insert if missing, never overwrite"), and the word "fixture" appears nowhere in the guides except `AI-EVALS.md` itself. Worse, §8's semantics are the opposite of what evals need: surviving state between scenarios is a named cause of nondeterministic evals (§9). The pattern used here — a named base world plus a sparse per-scenario delta, reconstructed from scratch every run — is invented, and is offered as the missing counterpart to §8. | AI-EVALS.md §3, SERVICE-API-PATTERNS.md §8 | 2026-08-14 | Proposed. |
| E-6 | **Mutation testing for eval suites.** `E2E-ACCEPTANCE-TESTING.md` §2 insists that a real assertion "only proves it can pass — not that it can catch anything", and requires a mutation pass. `AI-EVALS.md` has no equivalent, so an eval suite can be adopted wholesale without anyone ever proving it can fail. This repository closes it locally with deliberately broken agent variants the constraint layer must catch (`SPEC.md` §8.6). | AI-EVALS.md §4 | 2026-08-14 | Proposed. Implemented in Phase 4. |
| E-7 | **Write idempotency after an indeterminate failure.** A write that times out may or may not have landed. `grep -i idempot` across the standards returns only infrastructure provisioning — nothing governs a write path. Version 1.0.0 of the spec therefore specifies *reporting the uncertainty honestly* rather than *resolving it*, which is the weaker answer; a client-supplied idempotency key is the right one and belongs in `SERVICE-API-PATTERNS.md`. | SERVICE-API-PATTERNS.md §5–§6 | 2026-08-14 | Proposed. |
| E-8 | **Rate-limit partitioning for anonymous traffic.** `SERVICE-API-PATTERNS.md` §1 names two partition keys — authenticated user id, falling back to client IP — and a public anonymous demo has neither a user id nor safety in IP alone, which is the corporate-NAT collapse §1 opens with. A session key is needed, and a client-supplied session id is spoofable, so it can only narrow an IP bucket, never replace it. Not covered by the guide. | SERVICE-API-PATTERNS.md §1 | 2026-08-14 | Proposed. Needed in Phase 9. |

---

## Deviations deliberately NOT taken

The worked example this repository copies patterns from,
[`AureliusPromptus`](https://github.com/konradcinkusz/AureliusPromptus), carries
its own catalogued deviations in the constitution's §3a. They are listed here as
things this repository must **not** inherit while copying its shapes, because a
pattern and its known defect travel together unless someone writes down that they
should not.

| Not inherited | Principle | Why it matters here |
|---|---|---|
| `EnsureCreated` on a real database provider | P4 | Any schema this repository grows uses `MigrateAsync` from provider-specific migrations, applied by a hosted service after Kestrel starts. |
| One HS256 secret shared across services | P5 | This repository has no identity service and issues no tokens. If it ever needs one, it validates against a published JWKS endpoint and holds no key material. |
| Domain data in the shared kernel | P2 | `ServiceDefaults` here is plumbing only, held under the ~800-line ceiling by a CI size check rather than by intent. Fixtures, prompts and scenario data live with the service that owns them. |
| Credentials in a tracked helper script | P5 | Scripts read from the environment or refuse to run. Enforced by a pre-commit hook that fails when no scanner is available, and by a CI job over full history. |
| An E2E suite that is committed but never wired to CI | P13 | Every suite this repository adds is wired into `ci.yml` in the same pull request that creates it. A suite CI does not run is not evidence. |
