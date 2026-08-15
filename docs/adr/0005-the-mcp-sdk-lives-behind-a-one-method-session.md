# ADR-0005: The Model Context Protocol SDK lives behind a one-method session

- **Status**: Accepted
- **Date**: 2026-08-15
- **Phase**: 7 — the production story (the phase that builds the live integration)
- **Relates to**: P8 (optional dependencies degrade), P10 (extensibility is an interface plus a registration), P11 (anti-corruption at the edge), [ADR-0002](0002-mock-first-zero-credential-default.md), SPEC.md §2.1.1, §7.4

## Context

The live integration arrived in a repository with a specific and unusual
constraint: **there is no server to point it at.**

`https://mcp.factorialhr.com` is real, and reaching it needs an OAuth flow
against an account this repository deliberately does not hold — the public
deployment must never carry that credential, because it would act as a real
identity against a real workforce system ([ADR-0002](0002-mock-first-zero-credential-default.md)).
So the adapter is written from the protocol and the SDK's documentation, and the
first person to run it against a live server will be finding out what a real
payload looks like.

That changes what "good design" means here. Ordinarily the anti-corruption
boundary (P11) is justified by portability: a second backend should cost one
adapter file. Here it has to earn its place on a nearer argument — **whatever
cannot be tested without a server is code this repository never runs at all.**

Three things about the integration are worth testing, and none of them need a
server:

- The confirmation token is redeemed before the remote write, against a draft
  whose employee id came from `get_current_user` rather than from the arguments.
  Without this, SPEC §2.1.1's "in mock mode and in MCP mode alike" is a sentence
  about an adapter that does not enforce it.
- `only_for_self` survives a server that ignores the filter it was asked for.
- A failure is classified into the three answers SPEC §7.2 and §7.4
  distinguish — definitely not done, may or may not have been done, refused —
  because for a write those are different sentences to a human, and one of them
  forbids a retry.

The remaining piece — open an HTTP session, send a JSON-RPC call, read the first
text block — is about sixty lines and needs the real SDK.

## Decision

The SDK sits behind `IMcpToolSession`, one method wide:

```csharp
ValueTask<McpToolReply> CallAsync(
    string tool,
    IReadOnlyDictionary<string, object?> arguments,
    CancellationToken cancellationToken = default);
```

`McpClientSession` implements it and is the only file in `src/` that names
`ModelContextProtocol`. Everything else — mapping payloads into the workforce
model, redeeming the token, filtering the reply, classifying the failure — is
ordinary code that a forty-line fake exercises in milliseconds.

`grep -r ModelContextProtocol src/` returning exactly one file is the check, and
it is one anybody can run.

## Alternatives considered

### Implement `IWorkforceTools` directly against `McpClient`

**Why it is attractive:** one fewer interface, one fewer file, and the SDK
already has a perfectly good client abstraction. Adding a seam over somebody
else's seam is the kind of thing that reads as ceremony.

**Why it lost:** the tests. Faking `McpClient` means faking a sealed SDK type or
standing up an HTTP server that speaks Streamable HTTP and JSON-RPC — the first
is not possible, and the second means every test of the confirmation gate in MCP
mode drags a transport behind it. The behaviour worth testing would have been
buried under machinery, and in a repository whose subject is *evaluating agents*,
"we could not test it" is the wrong answer to arrive at.

### Register the SDK's `McpClient` in DI and inject it

**Why it is attractive:** the SDK ships DI extensions for exactly this, and the
container would own the lifetime.

**Why it lost:** it makes the vendor type part of the composition root's
vocabulary, which is where P11 says it must not be. It also connects eagerly at
resolution, and an unreachable server would then fail service start rather than
degrading (P8). The session connects lazily, on the first call, so the process
starts whatever the server is doing.

### Generate the adapter from the server's tool schemas at startup

**Why it is attractive:** `ListToolsAsync` returns the server's real tool list
with real JSON schemas, and a mapping built from that cannot drift from the
server.

**Why it lost:** it can drift from *this repository* instead, which is worse.
`WorkforceToolCatalog` classifies each tool read or write, and C-1 — "no
write-classified span before a confirmation event" — is derived from that
classification. A tool surface discovered at runtime is a classification decided
at runtime, and the first tool nobody classified is the first write that does not
count as one. The catalogue stays hand-written and the server's names are mapped
onto it by configuration.

## Consequences

**What this makes easy:**

- Testing the integration's actual behaviour with no server, no credential and no
  network. `FakeMcpToolSession` is forty lines and throws on an unarranged call,
  so a test cannot pass by arranging nothing.
- Keeping mode parity honest. Both modes are assembled by
  `WorkforceToolsFactory`, so "the mock and the adapter emit the same span shape"
  is three shared lines rather than a promise — and a test asserts the write span.
- Swapping the SDK. It is named in one file.

**What this makes hard:**

- Using SDK features the seam does not expose. Sampling, resources, prompts,
  progress notifications and structured content all stop at `CallAsync`. Adding
  one means widening the interface deliberately, which is the intended friction.

**What we accept:**

- **This has never run against a live server** (`docs/DEVIATIONS.md` D-10). The
  payload mapping is tolerant about shape and strict about content, and every
  failure message names the keys it looked for and the keys it found — written
  for the first real run, which is a log line rather than a debugger.
- **The adapter takes a bearer token; it does not acquire one**
  (`docs/DEVIATIONS.md` D-11). OAuth 2.0 with dynamic client registration is what
  a server like Factorial's actually speaks. Implementing that flow against a
  server nobody here can reach would be code written from documentation and
  tested by nothing — the same failure this ADR is arranged to avoid, one layer
  up.
- **Argument names are this repository's**, unlike tool names, which are
  configurable. A real server will differ on both, and this is the next thing the
  adapter needs.

## Revisit when

- A live server is reachable from a development machine. The payload mapping is
  the part most likely to be wrong, and the first real run is the test this ADR
  cannot write.
- The agent needs an MCP capability that is not a tool call — sampling, or a
  resource read. That is a deliberate widening of the seam, not a reason to
  remove it.
- The SDK gains a first-class fake or an in-memory transport. Then the seam buys
  less, and the argument for it becomes portability alone.
