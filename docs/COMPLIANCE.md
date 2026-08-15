# Compliance

The two checklists this repository is measured against, worked through line by line.
Every item is **yes**, **N/A with the reason**, or **no with a link to the row in
[`DEVIATIONS.md`](DEVIATIONS.md) that tracks it**. There is no fourth answer, and in
particular there is no "partially".

A checklist that is all ticks is a checklist somebody filled in. Two items below are
"no", four are N/A, and each of the six says why in the place a reader will look.

## 1. Reference architecture §3 — the constitution's compliance checklist

| # | Item | | Evidence |
|---|---|---|---|
| 1 | Declared in the AppHost with `WithReference`, `WaitFor` and `WithHttpHealthCheck` | **Partly, by shape** | `AppHost.cs` declares the one project with `WithHttpHealthCheck("/health")`. `WithReference`/`WaitFor` describe edges between resources and this system has one resource — there is nothing to reference and nothing to wait for. The showcase page is served by the same service, deliberately (see [§3](#3-the-frontend-is-not-a-second-service)). |
| 2 | Calls `AddServiceDefaults()` and `MapDefaultEndpoints()` | Yes | `Program.cs`, first and fourth lines of the manifest. |
| 3 | Exposes `/health` and `/alive`; the platform check points at `/health` | Yes | `MapDefaultEndpoints`; `flyio/demo.fly.toml` checks `/health`, not `/`. |
| 4 | Emits OTLP traces, metrics and logs | Yes | `ServiceDefaults` configures all three; the exporter activates when `OTEL_EXPORTER_OTLP_ENDPOINT` is set and degrades to nothing when it is not (P8). The trace is not diagnostics here — it is the interface Layer 1 grades. |
| 5 | Owns its database; no other service connects to it | **N/A** | There is no database. The world is a fixture file and the conversation state is in memory, both by design ([ADR-0002](adr/0002-mock-first-zero-credential-default.md)). |
| 6 | Schema applied by `MigrateAsync` in a hosted service | **N/A** | As above. |
| 7 | All configuration from environment variables; no secret in source, config or comment, with a secret scanner in CI | Yes | Every variable is documented once in [`secrets.env.example`](../secrets.env.example) with what degrades without it. gitleaks runs in CI and in a pre-commit hook that refuses the commit rather than warning. |
| 8 | Exactly one service holds a signing key; others validate against its JWKS | **N/A** | No authentication. The demo has no accounts and no user data; the access code is a spend control and [`flyio/SECRETS.md`](../flyio/SECRETS.md) says so rather than letting the word "code" imply otherwise. |
| 9 | The shared kernel holds no entity, DTO, enum, seed dataset, pricing constant or user-facing string — asserted by an architecture test and a CI size check | Yes | `ServiceDefaults` is 137 lines against the estate's ~700 ceiling, and the `architecture` job fails the build on both the size and the domain-vocabulary check. |
| 10 | Every optional integration has a working no-op or fallback | Yes | Four of them: no MCP configuration ⇒ mock tools; no model ⇒ deterministic composer; no OTLP endpoint ⇒ no exporter; no access code ⇒ live replies unavailable. Each logs which setting was missing. |
| 11 | Multi-stage Dockerfile; runtime image major = TFM major; listens on `:8080`; non-root | Yes | `src/AbsenceConcierge.AgentService/Dockerfile`: `sdk:10.0` → `aspnet:10.0` for `net10.0`, `ASPNETCORE_URLS=http://+:8080`, `USER $APP_UID`. |
| 12 | One `fly.toml`; `min_machines_running = 1` if another service calls it in-request | Yes | One config, and `0` is correct: nothing calls this service in-request except a browser, for which a cold start costs a second. |
| 13 | Outbound `HttpClient`s carry the standard resilience handler with explicit timeouts | Yes | `ConfigureHttpClientDefaults` in the kernel, so the model client gets it by default rather than by remembering. |
| 14 | `Program.cs` is a manifest; wiring is in `ServiceCollectionExtensions` | Yes | Seven capability lines and a comment saying why there is no Swagger. |
| 15 | Extension points are interfaces registered in DI, not base classes | Yes | `IWorkforceTools`, `IUtteranceInterpreter`, `IReplyComposer`, `ILlmProvider`, `IMcpToolSession`, `IAgentStep`, `IPromptLibrary`, `IDemoBudget`. No base class anywhere; adding a behaviour is a class plus a registration line. |
| 16 | Has a test project; the logic-bearing layer is covered | Yes | Two: unit tests, and the eval suite — which is a test project by every property that matters and is held to the same analyzer posture. |
| 17 | Built by the tag-driven workflow with path-based change detection | Yes | `.github/workflows/flyio.yml` fires on `v*` tags only, and gates the deploy on the eval suite. `check-change-coupling.mjs` is the path-based half: a prompt or agent-definition edit without a specification change fails the build. |
| 18 | Architectural decisions recorded in `docs/` | Yes | Five ADRs, each with the alternatives that lost. |

## 2. AI-EVALS §10 — per agent

| # | Item | | Evidence |
|---|---|---|---|
| 1 | Behaviour spec in-repo, versioned with the agent definition; behaviours, constraints, success criteria, out-of-scope, with negatives stated | Yes | [`SPEC.md`](SPEC.md) at 1.3.0, written before any agent code existed. §6 states each non-goal as a refusal with specified behaviour. The version appears in three places and CI compares them — it found them disagreeing on the first run. |
| 2 | Scenario dataset as data, covering happy / ambiguity / denied / adversarial (both injection paths) / degradation | Yes | 35 scenarios, all five classes, 313 assertions. Both injection paths: `adv-001` through the user's input, `adv-003` to `adv-007` through tool results. |
| 3 | Agent loop instrumented per OTel GenAI conventions; confirmations are trace events | Yes | `invoke_agent` / `execute_tool` spans, and `confirmation.shown` / `confirmation.received` as events with the draft's fields as attributes ([ADR-0003](adr/0003-agent-decisions-are-trace-attributes.md)). |
| 4 | Layer 1 asserts over traces: calls, arguments, ordering, absence, termination — no guard-then-bail, no swallowed failures, unimplemented = `Skip` | Yes | Twelve assertion types over the trace and nothing else. `validate-scenarios.mjs` enforces the discipline mechanically: no denied or adversarial scenario without an absence assertion, every scenario with a `termination` check. 57 of the 282 assertions say something did **not** happen. |
| 5 | Layer 2: rubric-anchored per-criterion judge that sees the trace; model and prompt pinned and versioned; **calibration against human labels recorded before scores gate** | **Partly** | The judge is built, pinned and versioned; the protocol is written and now exercised: 45 labels across 21 scenarios exist ([`CALIBRATION.md`](CALIBRATION.md)), written before the judge had ever produced a score anywhere, and κ computes on the first keyed run (`azure.yml` provisions the judge and runs it in the same dispatch). Still "Partly", for the stated reason: the first label set is an AI rater's, disclosed as such, and the checklist row says *human* — scores gate nothing until the owner's own labels exist. [D-9](DEVIATIONS.md) closes on that first keyed run. |
| 6 | Gates per §6: constraints hard-block at 100%; behaviour vs baseline; judge thresholds; prompts and definitions in change detection | Yes | 19 constraint scenarios hard-block; 13 behaviour scenarios are compared against a versioned baseline the harness refuses to read across versions; `check-change-coupling.mjs` covers `prompts/` and `agents/`. Judge thresholds exist and are inert until calibrated, per the row above. |
| 7 | Nightly matrix with baseline diffs; PR output is a diff, not a dashboard | Yes | `nightly.yml` runs the keyed Layer 2 pass; the pull request gets one sticky comment that leads with what changed against the baseline and says "still failing and recorded as such" where that is the truth. |
| 8 | Production sessions scored on the shared trace schema; worst sessions read on a cadence; low scorers converted to scenarios; constraint checks post-hoc with paging | **Partly** | The machinery now exists end to end: spans export to Application Insights (provisioned by `infra/azure/`, 100% sampling stated in the Fly config), a daily pass scores every turn on the shared schema, runs C-1 post-hoc — a violation fails the run, which is the pager — and uploads the worst sessions' span sets for extraction ([`PRODUCTION.md` §7](PRODUCTION.md#7-the-production-loop)). What remains is what only traffic can supply: a real failure, read by a person, becoming the first `origin.kind: production-trace` scenario. Tracked as **D-12**, narrowed. |
| 9 | Human review sampled and scheduled; findings become scenarios and rubric fixes | **Partly** | The schedule now exists and runs itself (`production-loop.yml`, daily, with the reading list in its summary), and the labelling pass has already produced findings that become rubric fixes ([`CALIBRATION.md`](CALIBRATION.md), "Who labelled first"). What no workflow can supply is the human keeping the cadence; [`PRODUCTION.md` §7](PRODUCTION.md#7-the-production-loop) records whose job that is, and D-12 stays open until the loop has carried a real finding end to end. |
| 10 | Every production incident has a scenario before it has a fix | **N/A, and armed** | There have been no incidents. What can be checked now is that the path is real rather than a promise: extraction is tested end to end, and an extracted scenario cannot enter the corpus until a human has replaced its `REVIEW:` marker. |

## 3. The frontend is not a second service

The estate's `FRONTEND-BFF.md` separates a frontend from the services it aggregates.
This repository serves one page from the agent service itself, and that is a decision
rather than an omission.

The separation buys something when a frontend aggregates several services: one origin
for the browser, one place to hold tokens, one hop that can fan out. This page talks
to a single endpoint on the host that served it. Splitting it would add a container to
keep patched, a CORS configuration to get wrong, and a second deployment to keep in
step — to protect against a fan-out that does not exist.

What it costs: if a second service ever arrives, this page moves. That is a smaller
change than the one avoided, and this paragraph is the record that it was a choice.

## 4. What is still open

Everything in [`DEVIATIONS.md`](DEVIATIONS.md), of which the ones a reader of a green
build should know about are:

- **D-1** — CodeQL is committed and inert until the repository is public.
- **D-9** — Layer 2 has never run against a live model. Zero scores.
- **D-10** — the MCP adapter has never run against a live server.
- **D-12** — production trace scoring, the review cadence and post-hoc constraint
  paging are not built.

Three of those four close the same way: by this thing running somewhere, with a
credential, in front of someone. None of them can be closed by a build.
