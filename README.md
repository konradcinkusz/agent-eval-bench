# agent-eval-bench

[![Ask me anything](https://flat.badgen.net/static/Ask%20me/anything?icon=github&color=black&scale=1.01)](https://github.com/konradcinkusz "Ask me anything")
[![GitHub license](https://flat.badgen.net/github/license/konradcinkusz/agent-eval-bench?icon=github&color=black&scale=1.01)](https://github.com/konradcinkusz/agent-eval-bench/blob/main/LICENSE "GitHub license")
[![Maintained](https://flat.badgen.net/static/Maintained/yes?icon=github&color=black&scale=1.01)](https://github.com/konradcinkusz/agent-eval-bench/commits/main "Maintained")
[![GitHub branches](https://flat.badgen.net/github/branches/konradcinkusz/agent-eval-bench?icon=github&color=black&scale=1.01)](https://github.com/konradcinkusz/agent-eval-bench/branches "GitHub branches")
[![GitHub commits](https://flat.badgen.net/github/commits/konradcinkusz/agent-eval-bench?icon=github&color=black&scale=1.01)](https://github.com/konradcinkusz/agent-eval-bench/commits/main "GitHub commits")
[![GitHub issues](https://flat.badgen.net/github/issues/konradcinkusz/agent-eval-bench?icon=github&color=black&scale=1.01)](https://github.com/konradcinkusz/agent-eval-bench/issues "GitHub issues")
[![GitHub pull requests](https://flat.badgen.net/github/prs/konradcinkusz/agent-eval-bench?icon=github&color=black&scale=1.01)](https://github.com/konradcinkusz/agent-eval-bench/pulls "GitHub pull requests")
[![CI](https://github.com/konradcinkusz/agent-eval-bench/actions/workflows/ci.yml/badge.svg)](https://github.com/konradcinkusz/agent-eval-bench/actions/workflows/ci.yml "CI")

A spec-first evaluation bench for tool-using agents — the reference implementation
of my [AI evaluation standard](https://github.com/konradcinkusz/architecture-standards/blob/main/docs/guides/AI-EVALS.md),
demonstrated on an HR **Absence Concierge**: an agent that books time off, and
stops for a human before it writes anything.

The agent is the excuse. **The eval bench is the deliverable.**

---

## Status

> **Phases 0–1 of 10 complete — the contract exists; the agent does not.**
>
> `docs/SPEC.md` and 32 scenarios are written and validated in CI. No agent code
> has been written, deliberately: the specification is the thing an implementation
> is measured against, and writing it second would make it a description instead.
>
> This README says which lines are built and which are planned. A README that
> describes a system that does not exist is worse than no README (P14's
> corollary), so the phase table is the honest answer to "what can I run today?" —
> and today that is the linters, the secret scanner, and the scenario validator.

| Phase | What it delivers | Status |
|---|---|---|
| 0 | Repository baseline: hygiene files, secret scanning, CI that lints a repo with no code | **Done** |
| 1 | `docs/SPEC.md` and 32 scenarios as data — the contract, before any agent code | **Done** |
| 2 | Skeleton: AppHost, agent service, ServiceDefaults, OpenTelemetry end to end, mock tools | Next |
| 3 | The agent loop: intent → dates → leave types → conflicts → draft → **confirmation gate** → execute | Planned |
| 4 | Eval harness, Layer 1 — deterministic assertions over captured traces | Planned |
| 5 | Eval harness, Layer 2 — rubric-anchored LLM judge, plus the calibration protocol | Planned |
| 6 | CI gates: constraints hard-block, behaviours vs baseline, one sticky PR comment with the diff | Planned |
| 7 | Production story: OTLP scoring integration, agent-as-code deployment, live MCP mode | Planned |
| 8 | `docs/FINDINGS.md` — numbers-first write-up of what the evals actually caught | Planned |
| 8b | Showcase frontend: one page, whose one special feature is the confirmation card | Planned |
| 9 | Public deployment, mock by default, scale-to-zero, live model behind an access code | Planned |

## Why this exists

Prompts get edited the way configuration gets edited — casually. A change to a
prompt, a model version, or a tool description can regress an agent's behaviour
with **no diff in your code**, and the usual defence is one good transcript pasted
into a channel.

This repository is the answer I hold my own projects to, built end to end so it can
be judged rather than described:

- A **behaviour spec** written before the agent, stating expected behaviours, hard
  constraints, success criteria, and what the agent refuses.
- A **scenario dataset as data** — YAML, not code — covering happy paths,
  ambiguity, denied paths, adversarial input (through the user *and* through tool
  results), and degradation.
- **Layer 1**: deterministic assertions over the execution trace. Not over the
  reply text — over which tools were called, with what arguments, in what order,
  and what was *not* done.
- **Layer 2**: a rubric-anchored LLM judge that sees the trace, with a pinned model
  and versioned prompts, calibrated against human labels before its scores gate
  anything.
- **CI gates**: constraint scenarios block at 100%; behaviour scenarios are
  measured against a recorded baseline; the pull request gets a diff, not a
  dashboard.

The standard itself is repository-agnostic and lives in
[`architecture-standards`](https://github.com/konradcinkusz/architecture-standards).
Its closing note currently says the first full worked example is under
construction. This repository is that example.

## The business context

I am applying to [Factorial](https://factorialhr.com) — a Barcelona-based HR and
business-management SaaS company — for the role
[AI Engineer, API & Integrations Team](https://careers.factorialhr.com/job_posting/ai-engineer-api-integrations-team-307535).
This repository is built against that role's own vocabulary, because the fastest
way to answer "can this person do the job" is to do a slice of it in public.

| What the role asks for | Where this repository answers it |
|---|---|
| **Spec Driven Development** — define expected behaviours, constraints and success criteria before shipping, and use them to guide implementation, iteration and evaluation | [`docs/SPEC.md`](docs/SPEC.md) exists and no agent code does. 16 behaviours, 7 hard constraints, 5 rubrics and 7 refusals, each citing the scenarios that prove it |
| **AI Skills** — reusable, well-scoped capabilities that automate and take actions *safely* | One capability, scoped narrowly: request time off. "Safely" is the confirmation gate, and it is a hard constraint with a trace event, not a prompt instruction |
| **Evals** — measure quality, correctness and reliability with automated and human-in-the-loop evaluation | The two-layer harness, the CI gate, and a calibration protocol that records judge/human agreement before the judge is allowed to block anything |
| **RAG and grounding** — ground responses in trusted, up-to-date company data, balancing probabilistic models with deterministic sources of truth | Leave types, balances and existing bookings come from tool results, never from the model. Grounding is a judged criterion, and the judge reads the trace so it grades grounding rather than fluency |
| **Human-in-the-loop agentic workflows** — combine LLMs, rules and user oversight, keeping humans in control of critical decisions | The agent drafts, shows a summary, and **stops**. The write happens in a later turn, only after an explicit confirmation event |
| **Stack-agnostic engineering** — solid fundamentals in any language; what you built matters more than the stack | Built in .NET because that is my stack. Every eval artifact — spec, scenarios, rubrics, baselines — is stack-neutral YAML, JSON and Markdown, and would port to Ruby or TypeScript unchanged |

Factorial's engineering runs on Ruby on Rails and React. **This is not a
contribution to their codebase** — it is an external client of their platform,
which is precisely what an API and integrations team exists to enable. The
integration target is their public
[MCP server](https://mcp.factorialhr.com) (Streamable HTTP, OAuth 2.0 with dynamic
client registration), which acts as the authenticated user and enforces that
user's permissions on every call. The write this agent is built around is their
time-off request tool.

## The agent, in one paragraph

A user says *"I'm sick today and probably tomorrow"* — or *"book me Friday off"*.
The agent resolves the dates in the user's timezone, fetches the available leave
types, checks existing leaves for conflicts, drafts the request, shows a summary
and **stops for explicit human confirmation**. Only then does it execute the write,
and it reports the outcome grounded in what the tools actually returned. Denied
paths — no permission, unknown leave type, a request that is out of scope — refuse
cleanly and are asserted twice: the refusal happened, *and* the call did not. Tool
failures degrade into partial output with a note, never a fabricated result and
never a silent retry loop.

Everything interesting is in the second half of that paragraph.

## Running it

Today, from a fresh clone:

```bash
./scripts/setup.sh                # prerequisites, git hooks, .env — about a minute
./scripts/scan-secrets.sh         # the CI secret scan, run locally
npm install && npm run lint       # docs lint, link check, and scenario validation
npm run validate:scenarios        # just the eval corpus: 32 scenarios, 5 classes
```

Reading order for a visitor with ten minutes: [`docs/SPEC.md`](docs/SPEC.md) §1
and §4, then
[`hap-001`](evals/scenarios/happy/hap-001-sick-today-and-tomorrow.yaml) (the
reference path) and
[`adv-003`](evals/scenarios/adversarial/adv-003-injection-via-leave-type-name.yaml)
(an injection arriving inside data the agent asked for). The spec's hard
constraints plus those two scenarios are the whole idea.

From Phase 2 onward, the same fresh clone will run the whole thing:

```bash
dotnet run --project AbsenceConcierge.AppHost
```

**With zero credentials.** That is a designed property, not a temporary state:
mock workforce tools with fictional fixtures, replayed model responses, and a full
Layer-1 eval suite that runs green offline. Every credential is optional, every
optional integration degrades with a working fallback, and an absent credential
produces an explicit skip with a reason — never a silent pass. The reasoning is in
[ADR-0002](docs/adr/0002-mock-first-zero-credential-default.md); the complete
variable list, each with what degrades without it, is in
[`secrets.env.example`](secrets.env.example).

## Repository layout

Directories that exist today, and those the phase plan will add.

```text
.github/            CI, secret scanning, PR and issue templates, CODEOWNERS
docs/
  SPEC.md           the behaviour contract — behaviours, constraints, rubrics
  adr/              architecture decision records
  DEVIATIONS.md     where this repo departs from the standards — dated and reasoned
evals/
  schema/           the scenario contract, as strict JSON Schema
  fixtures/         shared fictional worlds; scenarios write only the delta
  scenarios/        32 scenarios across five classes
scripts/            setup, hooks, validators, and local mirrors of the CI jobs
                    ────────── planned ──────────
docs/CALIBRATION.md how judge scores are checked against humans   (Phase 5)
docs/FINDINGS.md    what the evals actually caught, in numbers    (Phase 8)
evals/
  rubrics/          versioned judge prompts, pinned model         (Phase 5)
  baselines/        recorded pass state a regression is measured against (Phase 4)
agents/
  absence-concierge/definition.json    the agent as code          (Phase 2)
```

## How this repository relates to the standards

It does not re-derive them. The architecture is fixed and documented in
[`architecture-standards`](https://github.com/konradcinkusz/architecture-standards):
.NET Aspire with the AppHost as composition root, one thin `ServiceDefaults`
kernel, container per service, OpenTelemetry first, tag-driven CI/CD to Fly.io.
This repository reads that constitution and follows it.

Where it must depart, the departure is recorded — dated, reasoned, with a closing
condition — in [`docs/DEVIATIONS.md`](docs/DEVIATIONS.md), and the amendment it
implies is proposed back to the standard. That file also lists what this
repository deliberately does *not* inherit from the worked example it copies
patterns from, because a pattern and its known defect travel together unless
someone writes down that they should not.

## Non-goals

Stated so that scope creep has something to fail against.

- **No frontend beyond one page.** The showcase is a single chat page whose one
  special feature is the confirmation card. It is presentation only — the
  confirmation gate itself lives in the agent service, because the agent's good
  behaviour is UX and the service boundary is security.
- **No multi-agent orchestration.** One agent, one capability, evaluated properly.
- **No payments, quota or identity service.** Those are solved in the standards;
  this repository links to them rather than rebuilding them.
- **No fork of the standards.** Deviations are recorded, not forked.
- **No real personal data.** Every fixture is fictional, and the issue templates
  ask contributors to confirm it.
- **Multi-user approval chains and edits to existing leaves are out of scope for
  the agent** — and the refusal itself is specified and tested, rather than left
  as an implicit gap.

## License

[MIT](LICENSE).
