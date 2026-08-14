// ─────────────────────────────────────────────────────────────────────────────
//  The composition root (P1).
//
//  One command brings the system up: `dotnet run --project src/AbsenceConcierge.AppHost`.
//  Every resource the system needs is declared here, with the edges between them.
//
//  Two rules the estate learned the hard way, both relevant already:
//
//   • The AppHost is not the production topology. Production is described by the
//     platform's own configuration (fly.toml, workflow env). Treating this file as
//     a second runtime is what produced the drift catalogued in the worked
//     example's review.
//
//   • Nothing here carries a secret. The demonstrated path needs none at all — the
//     mock workforce tools and replayed model responses are the default, so a
//     fresh clone runs with an empty .env (ADR-0002).
// ─────────────────────────────────────────────────────────────────────────────

var builder = DistributedApplication.CreateBuilder(args);

var agentService = builder.AddProject<Projects.AbsenceConcierge_AgentService>("agent")
    .WithHttpHealthCheck("/health");

// Phase 8b adds the showcase frontend here, referencing the agent service so the
// browser only ever talks to its own origin (FRONTEND-BFF).
_ = agentService;

builder.Build().Run();
