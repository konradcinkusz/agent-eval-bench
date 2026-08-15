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
| `Demo__AccessCode` | `flyctl secrets set` | Live replies are **unavailable**, not open. A missing secret is the normal state of a fork and a preview environment. |
| `FLY_API_TOKEN` | GitHub repository secret | The deploy workflow cannot run. Nothing else is affected. |

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

## Why the access code is not authentication

It is a spend control, and calling it anything else would set the wrong
expectations. It answers "is this somebody I gave a code to?" — enough to keep a
paid model off a public endpoint, and nowhere near enough to protect data. There is
no data here to protect: every visitor sees the same fictional company, nothing is
stored, and nothing is written anywhere.

The control that actually bounds cost is the daily token budget, which no amount of
code-sharing moves.

## Rotation

There is no secret here whose exposure loses anything but money, and the amount is
bounded by the daily budget. Rotate the access code by setting it again; rotate the
model key in the provider's portal and set it again. Neither needs a redeploy —
`flyctl secrets set` restarts the machines.
