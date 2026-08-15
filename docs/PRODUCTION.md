# Production

What changes when this agent runs somewhere real, and what silently stops
working if you are not careful. Everything here is about the same claim, said
three ways:

> **The trace the eval suite grades is the trace production emits.**

That is not a slogan about observability. Layer 1 asserts over spans and events
and nothing else ([ADR-0003](adr/0003-agent-decisions-are-trace-attributes.md)),
so a production trace is, by construction, a gradeable one — and a production
failure can become a scenario by extraction rather than by somebody's
reconstruction of it. The sections below are what that costs, and end with how the
thing is deployed and what a visitor can reach.

## 1. The eval contract is a production contract

`AgentDiagnostics` names every attribute and event the suite reads. A production
deployment that drops one of them does not break the agent; it breaks the ability
to say anything about the agent afterwards. Two settings do that quietly.

### Sampling

`ActivitySource.StartActivity` returns `null` when a trace is not sampled, and
every emission site in this codebase is written `activity?.SetTag(...)` — as it
must be. So a ratio sampler does not degrade the trace. It removes it, for that
proportion of turns, and the agent behaves identically.

The consequence is specific: **at 10% sampling, nine out of ten incidents have no
trace to extract a scenario from.** AI-EVALS.md §3 asks for a scenario before a
fix; the trace is the input to that, and sampling decides whether it exists.

The recommendation is to sample this service at 100% and control cost with
retention instead. A time-off agent's traffic is human-paced — one turn per
employee per absence — and the span count per turn is single digits. This is not
a high-volume path where sampling is the only lever.

### Attribute value length

`OTEL_ATTRIBUTE_VALUE_LENGTH_LIMIT` truncates attribute values at export. It is
unset by default in the .NET SDK, and setting it is a reasonable instinct for a
service whose spans carry free text.

`workforce.tool.result_ids` is the attribute that would be cut. It carries the
identifiers a tool returned, and it exists so that C-5 — "an identifier in a
write came from an earlier tool result" — is answerable from the trace rather
than from trust ([SPEC §2.2](SPEC.md#22-trace-events)). Truncate it and the
grounding assertion becomes unevaluable on exactly the traces worth grading, with
nothing going red to say so.

If a limit is needed, it needs to be above the longest identifier list this agent
produces, which is bounded by the number of leave types a tenant has.

### What is deliberately not in a span

The confirmation token, which authorises a write and is therefore a credential;
and any error text a remote server returned, which is free text that may name a
person. Both go to the log instead. This matters in production because a span
goes to a collector and a log line does not have to.

## 2. From a production trace to a scenario

`ScenarioExtractor` reads a recorded trace and writes a scenario file, deriving
every assertion mechanically — including one `tool_not_called` for every tool
that was **not** called, which is the half that gets forgotten when a person
reconstructs an incident from memory.

What it produces is a characterisation: *this is what the agent did*. That is not
yet a test, because a test says what the agent **should** do. So the emitted
`title` and `why` carry a `REVIEW:` marker, and
[`scripts/validate-scenarios.mjs`](../scripts/validate-scenarios.mjs) refuses any
scenario still carrying one. An extraction cannot reach the corpus unread, which
is the only thing standing between "we captured the incident" and "we enshrined
the bug as expected behaviour".

Two things extraction cannot recover, and both need a human:

- **The world.** A scenario's `fixture.overrides` block describes the data the
  turn ran against. A trace records what the tools returned, not the state behind
  them, so a trace from a world that differed from the base fixture needs that
  delta written by hand.
- **The class.** Whether an incident belongs in `adversarial/` or `degradation/`
  is a judgement about what went wrong, and the corpus rules gate on it —
  `denied` and `adversarial` scenarios must be `constraint`-gated, which
  hard-blocks the build.

The round trip is tested: a corpus scenario is run, a scenario is extracted from
its trace, written as YAML, read back through the corpus loader and run again —
and then put in front of the deliberately broken agent that its source scenario
catches. A scenario derived from a passing run could easily be a set of
tautologies, and a large green suite of tautologies is worse than no suite.

## 3. The agent as code

[`agents/absence-concierge/definition.json`](../agents/absence-concierge/definition.json)
is what a provisioner reads. It is behaviour with no code diff, so it is checked
like code — [`scripts/validate-agent-definitions.mjs`](../scripts/validate-agent-definitions.mjs),
in the `lint-docs` job:

| Check | What it stops |
|---|---|
| Strict schema, `additionalProperties: false` | A mistyped key that a provisioner silently ignores |
| `slug` matches its directory | Two names for one agent |
| One version in three places | A baseline recorded against a number the definition does not claim. This found a `specVersion` two versions behind on its first run |
| `allowedTools` = `WorkforceToolCatalog` | A tool the code has and the definition does not, or the reverse |
| `requireApproval` splits read/write exactly as the catalogue does | The third enforcement layer quietly disagreeing with the other two about which call books somebody's leave |
| `serverUrl` is a `${VAR}` placeholder | A committed endpoint |

`model` is `null` and that is not an omission. **A provisioner reading `null` must
refuse.** Substituting a default would be a behaviour change with no diff, which
is the failure this whole repository is arranged against; the validator requires
a `metadata.modelSelection` note saying so, because an unexplained null is
indistinguishable from a field somebody forgot.

## 4. Live MCP mode

`WorkforceTools:Mode = Mcp` points the agent at a Model Context Protocol server
instead of the in-memory mock. It is a **local and development-only** mode.

The public deployment carries none of the `WorkforceTools:Mcp:*` settings, so the
branch is unreachable there rather than merely switched off. With `ServerUrl` or
`AccessToken` absent, the service logs which one is missing, runs on the mock, and
starts normally (P8). It never half-configures a client and tries anyway.

Two properties are enforced by the adapter rather than trusted to the server:

- **The confirmation gate.** The token is redeemed before the remote write,
  against a draft whose employee id came from `get_current_user` rather than from
  the arguments. The token is spent whatever the server then answers, so an
  indeterminate write has nothing left to retry with. A real workforce system
  probably has an approval step of its own; what it does not have is any
  knowledge of *this conversation*.
- **`only_for_self`.** The actor's id is sent as a filter and the reply is
  filtered again on the way back.

A write's failure is reported from the division in
[SPEC §7.4](SPEC.md#74-which-failure-is-which-at-the-transport): a refused
connection never reached the server and is a definite failure; a timeout may have
booked the leave and is reported as unknown. The default for anything unclassified
is unknown, because the cost of a wrong "it definitely failed" is a human filing
the same leave twice.

**This has never run against a live server** ([D-10](DEVIATIONS.md)), and the
adapter takes a bearer token rather than acquiring one through OAuth
([D-11](DEVIATIONS.md)). Both are recorded rather than glossed. The design that
makes that tolerable is [ADR-0005](adr/0005-the-mcp-sdk-lives-behind-a-one-method-session.md):
the SDK sits behind a one-method session, so everything above it — the token, the
filter, the failure classification, the payload mapping — is exercised by a fake
in milliseconds rather than being code nothing in this repository ever runs.

## 5. What a first live run should expect to find

The payload mapping is the part most likely to be wrong, so its failures are
written for that run rather than for a debugger. Every message names the keys the
adapter looked for and the keys the object actually had — never a value, because
the message reaches a log and a value may be somebody's name:

```text
list_leaves[].employee_id: looked for [employee_id, employee],
found an object with [id, leaveTypeId, startOn, endOn, state].
```

Key lookup already ignores case and separators, unwraps a list from
`data`/`items`/`results`, accepts an identifier that arrived as a number, and
reads a timestamp as the date the server meant in the offset it sent. What it
refuses to guess is a missing required field — a booking with no employee id
would make the `only_for_self` filter pass for somebody else's leave.

Tool names are configurable (`WorkforceTools:Mcp:ToolNames:*`) because that is
the boundary's job. Argument names are not, yet, and that is the next thing this
adapter needs.

## 6. Deploying it

One app, one machine, scaled to zero: [`flyio/demo.fly.toml`](../flyio/demo.fly.toml).
It ships on a `v*` tag and never on a branch push, and the workflow's first job is
the eval suite — **an agent deployment whose evals are advisory has no gate.** That
job needs no credential, which is the property that lets it be a release gate rather
than a nightly hope ([ADR-0002](adr/0002-mock-first-zero-credential-default.md)).

After the deploy reports success, the workflow checks two things the deploy itself
cannot:

- **The page answers 200.** Fly's own check points at `/health`, deliberately — a
  page can serve 200 with a broken script behind it, so a health check pointed at
  `/` would pass on a white screen. That means nothing has verified `/` until this
  step does.
- **The security headers survived.** What removes a header in production is a proxy,
  a platform default or a middleware ordering change, and a unit test sees none of
  those.

### The page

Three static files served by the agent service itself: one HTML, one stylesheet, one
script, no build step and no framework. A page with one interaction does not need a
dependency tree, and the tree is the part that needs patching for the next five
years.

Two properties are worth naming because both are easy to lose:

- **The confirmation card is rendered from structured data**, not parsed out of the
  reply. A page that read dates back out of prose would be a second implementation
  of the draft, free to disagree with the one a human is about to approve.
- **Every value is set with `textContent`, never `innerHTML`.** The fixtures contain
  deliberate injection attempts — `adv-003` hides an instruction inside a leave type
  name — and a page that rendered them as markup would be the one place in this
  repository where that content becomes executable.

The content security policy is `default-src 'none'` with `connect-src 'self'` and
**no `unsafe-inline`**, which is why the CSS and the script are separate files.
Inlining them would have been one fewer request and would have cost the strictest
clause in the policy — the trade that quietly happens on most pages, and the reason a
strict CSP is rarer than a CSP.

### Live replies, and the three things that gate them

The live composer needs a credentialed provider, an access code, and budget left
today. Each is checked in a different place and each fails closed; a missing secret
is the normal state of a fork, so unset means *unavailable* rather than *open*.

The access code is a **spend control, not authentication**, and
[`flyio/SECRETS.md`](../flyio/SECRETS.md) says so rather than letting the word
"code" imply otherwise. There is no data here to protect: every visitor sees the
same fictional company and nothing is stored. What bounds the cost is the daily
token budget, which no amount of code-sharing moves — and which is held in memory,
so a scale-to-zero restart resets it ([D-13](DEVIATIONS.md)).

### Never deployed

No Fly account is wired to this repository, so the workflow above has never run.
The configuration is reviewable and the checks are real; whether the app comes up is
not something a green build can tell you, and this paragraph is here instead of a
badge that would imply otherwise.
