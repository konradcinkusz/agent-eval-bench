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
//  Phase 2 wires the skeleton: telemetry end to end, health, and the mock
//  workforce tools behind their anti-corruption interface. The agent loop itself
//  arrives in Phase 3, against the contract already written in docs/SPEC.md.
// ─────────────────────────────────────────────────────────────────────────────

var builder = WebApplication.CreateBuilder(args);

// Telemetry, health, service discovery, resilience. Every service calls this (P2a).
builder.AddServiceDefaults();

// The agent's own ActivitySource, registered with the tracer the kernel configured.
builder.Services.AddAgentTelemetry();

// The workforce tool surface: one interface, mock by default, zero credentials.
builder.Services.AddWorkforceTools(builder.Configuration);

var app = builder.Build();

// /health (readiness) and /alive (liveness).
app.MapDefaultEndpoints();

// Read-only visibility into the loaded world. This is what makes the skeleton
// demonstrable before the agent exists — and what the integration test drives to
// prove a tool-call span reaches an exporter.
app.MapWorkforceEndpoints();

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
