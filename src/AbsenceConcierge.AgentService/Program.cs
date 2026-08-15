using AbsenceConcierge.AgentService.Endpoints;
using AbsenceConcierge.AgentService.Extensions;

// ─────────────────────────────────────────────────────────────────────────────
//  Program.cs is a manifest (P9).
//
//  It reads as a list of capabilities, not as configuration code. Every block is
//  one call into ServiceCollectionExtensions in this service. The estate's own
//  comparison makes the case: 130 lines for the service with four orchestrators
//  and a step pipeline, against 399 for the one doing the same job inline — and
//  the second is the harder file to change.
//
//  Phase 2 wired the skeleton: telemetry end to end, health, and the mock
//  workforce tools behind their anti-corruption interface. Phase 3 added the agent
//  itself — a step pipeline whose order is the specification, running against the
//  behaviour contract in docs/SPEC.md and with no model configured by default.
//  Phases 8b and 9 add the public face: one page, security headers, a rate limit,
//  and a live composer that exists only where a model and an access code both do.
//
//  There is deliberately no Swagger/OpenAPI UI at any point below. It is
//  information disclosure — the route map, the DTO shapes and the validation rules,
//  gift-wrapped (SECURITY-REVIEW.md §7) — and "off in production" is a switch
//  somebody flips the wrong way during a debugging session. This service has four
//  routes and they are documented in docs/PRODUCTION.md.
// ─────────────────────────────────────────────────────────────────────────────

var builder = WebApplication.CreateBuilder(args);

// Telemetry, health, service discovery, resilience. Every service calls this (P2a).
builder.AddServiceDefaults();

// The agent's own ActivitySource, registered with the tracer the kernel configured.
builder.Services.AddAgentTelemetry();

// The workforce tool surface: one interface, mock by default, zero credentials.
builder.Services.AddWorkforceTools(builder.Configuration);

// The agent: a step pipeline, an injected clock, and no model unless one is configured.
builder.Services.AddAbsenceConciergeAgent(builder.Configuration);

// The public demo's ceilings: an access code, a daily token budget, and — only when
// a model is actually configured — the live composer that spends against it.
builder.Services.AddDemoMode(builder.Configuration);

// Every route a stranger can reach, not only the expensive one. Partial coverage is
// the recorded failure mode (SECURITY-REVIEW.md §9).
builder.Services.AddDemoRateLimiting(builder.Configuration);

var app = builder.Build();

// First in the pipeline, so a response served from any later branch — including an
// error page and a 429 — carries them.
app.UseShowcaseSecurityHeaders();

app.UseRateLimiter();

// One page, three files, no build step. Its one interaction is the confirmation card.
app.MapShowcase();

// /health (readiness) and /alive (liveness).
app.MapDefaultEndpoints();

// Read-only visibility into the loaded world. This is what makes the skeleton
// demonstrable before the agent exists — and what the integration test drives to
// prove a tool-call span reaches an exporter.
app.MapWorkforceEndpoints();

// One turn of the agent. The only route to a write, and it runs through the gate.
app.MapAgentEndpoints();

await app.RunAsync();

/// <summary>
/// Exposed so the integration tests can host this service with
/// <c>WebApplicationFactory</c>. Top-level statements generate an internal Program
/// class; this makes it addressable without an <c>InternalsVisibleTo</c> that would
/// open up rather more than the entry point.
/// </summary>
public partial class Program
{
}
