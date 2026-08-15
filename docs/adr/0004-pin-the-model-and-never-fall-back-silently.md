# ADR-0004: Pin the agent model and the judge model separately, and never fall back silently

- **Status**: Accepted
- **Date**: 2026-08-15
- **Phase**: 3 — the agent loop (the first phase that could call a model)
- **Relates to**: P8 (optional dependencies degrade), P11 (anti-corruption at the edge), P5 (no secrets in the repository), AI-EVALS.md §5, SPEC.md §8.2, §8.4

## Context

Phase 3 is the first point at which a language model could be called, so the
provider question had to be settled before any of it was written. The stated
preference was Azure OpenAI as the default, a cheaper older model such as a
GPT-4.5-class deployment, "Sonnet 5 if available, with fallback", and secrets held
in `dotnet user-secrets` locally and a GitHub environment secret in CI.

Three facts shape the answer, and the first of them corrects the premise.

**Azure OpenAI and Claude models are not the same surface.** Azure OpenAI serves
OpenAI models and is addressed by *deployment name* — the string you choose when
you create the deployment, not a model id. Claude models are served through
**Microsoft Foundry**, which is a different endpoint with its own client library,
billed through the Microsoft Marketplace at standard Anthropic rates. "Azure
OpenAI, with a Sonnet 5 option" is therefore two adapters and two credentials, not
one configuration flag. Current published rates, for the budget in SPEC §8.1:
Sonnet 5 at $3 / $15 per million tokens in and out (an introductory $2 / $10
through 2026-08-31), Haiku 4.5 at $1 / $5, Opus 5 at $5 / $25.

**A silent fallback would invalidate the baseline.** This is the part worth being
firm about. An eval baseline records a pass rate against a specific configuration.
If a run that could not reach Sonnet 5 quietly answered with a GPT-4.5-class
deployment instead, the recorded number would describe a system nobody chose, and
the next run's diff would be attributed to the change under review. It is exactly
the defect AI-EVALS.md §5 names for an unpinned judge — *a measuring stick that
changes length* — one layer over, and SPEC §8.4 already applies the same rule to
fixtures: editing the world forces a re-baseline.

**The judge is a separate pin from the agent.** If both move together, a changed
score cannot be attributed: both sides of the comparison moved at once.

## Decision

The model sits behind one interface, `ILlmProvider`, with no vendor type in any
signature. `Llm:Provider` selects `None` (the default), `AzureOpenAI`, or
`AnthropicFoundry`; `Llm:Model` carries an Azure **deployment name** or a Foundry
model id depending on which; `Llm:JudgeModel` pins the judge independently of the
agent. Falling back to a different model is permitted only when the provider
reports the model that actually answered and the caller records it on the span —
a fallback that does not appear in the trace is forbidden, and a baseline is
partitioned by the model that produced it. No key is present in the repository:
locally the values come from `dotnet user-secrets` against the `UserSecretsId`
already declared in the service's project file, and in CI from a GitHub
environment secret. An absent key means the capability is unavailable, not that a
substitute answers quietly (P8).

## Alternatives considered

### One provider, with automatic failover between models

**Why it is attractive:** It is what the original request asked for in spirit —
"Sonnet 5 if available, with fallback to 4.5" — and it is what a production
service should usually do. Availability wobbles, and a request that fails because
a preferred model was busy is a worse outcome than a request answered slightly
differently.

**Why it lost:** The repository's product is the measurement, not the answer. A
service optimises for the request succeeding; an eval bench optimises for the
number meaning something, and those two goals point in opposite directions
precisely at the moment a fallback fires. The compromise adopted is not "no
fallback" but "no *invisible* fallback": the provider may substitute, and the
substitution is a recorded attribute that partitions the baseline. Production
users of this pattern can keep the failover and lose nothing.

### Azure OpenAI only, and drop Claude models from scope

**Why it is attractive:** One adapter, one credential, one bill, and the stated
default. It would have removed a whole surface from Phase 3.

**Why it lost:** The judge and the agent should not be the same model family
where it can be avoided — a judge that shares a model's failure modes will
forgive them — and AI-EVALS.md §5's calibration work in Phase 5 is more
convincing across two families than one. Keeping the second adapter behind the
same interface costs one class and is the difference between a demonstration and
a claim.

### Read the key from an environment variable the service names itself

**Why it is attractive:** Fewer moving parts than user secrets, and it is what a
container does anyway.

**Why it lost:** Nothing, in the end — this *is* what happens in the container,
because configuration binds environment variables by convention. What was
rejected is a variable read directly out of `Environment.GetEnvironmentVariable`
in application code, which bypasses the options system, cannot be overridden in a
test, and is how a credential ends up logged by something that thought it was
reading configuration.

## Consequences

**What this makes easy:** Running everything with no credentials, which stays the
default. Adding the second provider later without touching a step, an assertion or
a scenario. Reading a baseline and knowing what produced it.

**What this makes hard:** Comparing two runs that used different models — which
is the point. They are different measurements and the trace says so, rather than
averaging into one number that describes neither.

**What we accept:** That a nightly run can fail outright when a pinned model is
unavailable, instead of degrading to a cheaper one and reporting a number. A
missing run is an honest gap; a substituted one is a wrong answer wearing the
right label.

## Revisit when

Foundry's feature set stops being mostly in preview, or when the nightly matrix
grows past two models — at which point "which model produced this row" needs to be
a first-class column in the baseline file rather than an attribute on a span.
