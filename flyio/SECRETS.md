# Secrets, and what happens without each one

Nothing in this directory holds a value. This file says which values exist, where
they live, and — the part that usually goes unwritten — **what the deployment does
when one is missing**, because "optional" without a consequence is not a decision
anybody can make.

The short version: **every secret here is optional, and the demo is fully usable
with none of them set.** That is the property [ADR-0002](../docs/adr/0002-mock-first-zero-credential-default.md)
exists to protect.

## The list

| Secret | Set with | Absent ⇒ |
|---|---|---|
| `Llm__ApiKey` | `flyctl secrets set` | No model is constructed. Replies are written by the deterministic composer and the page's banner says so. |
| `Llm__Endpoint` | `flyctl secrets set` | As above — the provider reports itself unconfigured rather than half-configured. |
| `Llm__Model` | `[env]` or a secret | As above. This is the Azure OpenAI **deployment name**, not a model id; conflating the two is the usual first-call failure. |
| `Demo__AccessCode` | `flyctl secrets set` | Optional here: this demo runs **open** live mode (`Demo__AllowLiveWithoutCode` in `demo.fly.toml`), bounded per visitor and per day. A code, when set, additionally exempts its holder from the per-visitor allowance. On a deployment *without* the open flag, an absent code means live replies are **unavailable**, not open. |
| `APPLICATIONINSIGHTS_CONNECTION_STRING` | `flyctl secrets set` (written by the Azure provisioning workflow) | Traces are produced but exported nowhere — the demo works, and the production loop (`docs/PRODUCTION.md` §1) has nothing to read. |
| `FLY_API_TOKEN` | GitHub environment secret (`demo`) | The deploy workflow cannot run. Nothing else is affected. |

```bash
flyctl secrets set --app agent-eval-bench-demo \
  Llm__ApiKey=… Llm__Endpoint=… Demo__AccessCode=…
```

Secrets are set by name on the command line and never written into a file in this
repository. The estate's recorded incident is exactly the other thing: live
credentials pasted into a tracked helper script, because an inline literal was the
path of least resistance.

## What is deliberately not deployable

**`WorkforceTools__Mcp__*` is never set on this app.** The Model Context Protocol
integration authenticates as a real identity against a real workforce system, and a
public endpoint holding that credential is a public endpoint acting as somebody. It
is a local and development-only mode; the app carries none of its settings, so the
branch is unreachable rather than switched off ([ADR-0005](../docs/adr/0005-the-mcp-sdk-lives-behind-a-one-method-session.md)).

If those settings ever *were* set here, the service would still start and still work
— it would simply be doing something nobody asked it to. The control is that the
values do not exist on this app, and this paragraph is why.

## Why there is no authentication, and what bounds the spend instead

The demo is deliberately open: no account, no sign-in, and on this deployment no
access code either. That is a decision, not an omission — a demo behind a code is a
demo most visitors never see working, and there is no data here to protect: every
visitor sees the same fictional company, nothing is stored, and nothing is written
anywhere.

What replaces authentication is a stack of ceilings, each of which fails closed:

| Ceiling | Bounds |
|---|---|
| `Demo__DailyOutputTokenBudget` | The bill. Shared, daily, and no number of visitors moves it. |
| `Demo__LiveTurnsPerClientPerDay` | One visitor's share of it. Past it, that visitor's replies go deterministic; the demo keeps working. |
| `Demo__RequestsPerMinutePerClient` | One address's request rate, on every route a stranger can reach. |
| `Demo__MaxConcurrentRequests` | What all visitors together can hold open at once. Health probes are exempt. |
| `Demo__MaxConversations` / `Demo__MaxTurnsPerConversation` / `Demo__MaxRequestBodyBytes` | The process's memory: bounded map, bounded turns, bounded payload. |

The access code still exists as an operator's convenience — a holder is exempt from
the per-visitor allowance — but nothing about the demo requires a visitor to hold
anything. It answers "is this somebody I gave a code to?", which is a spend
question, never an identity one.

## Rotation

There is no secret here whose exposure loses anything but money, and the amount is
bounded by the daily budget. Rotate the access code by setting it again; rotate the
model key in the provider's portal and set it again. Neither needs a redeploy —
`flyctl secrets set` restarts the machines.
