using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Diagnostics.HealthChecks;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Logging;
using OpenTelemetry;
using OpenTelemetry.Metrics;
using OpenTelemetry.Trace;

namespace Microsoft.Extensions.Hosting;

/// <summary>
/// The shared kernel: telemetry, health, service discovery and resilience.
///
/// P2 — this is a shared <em>kernel</em>, not a shared <em>domain</em>. It contains no
/// entity, no DTO, no enum, no seed dataset, no pricing constant and no user-facing
/// string, and the CI size check in ci.yml enforces the ~800-line ceiling mechanically
/// rather than by intent. The estate has twice watched a plumbing library grow into a
/// domain library — most recently the worked example this repository copies its shape
/// from, whose kernel now carries 607 lines of seeded domain prompts.
///
/// In particular, the agent's own vocabulary — tool names, confirmation events, turn
/// outcomes — lives in the service that owns it, not here. A future second service
/// would share the plumbing below and none of that.
/// </summary>
public static class Extensions
{
    private const string HealthEndpointPath = "/health";
    private const string AlivenessEndpointPath = "/alive";

    /// <summary>
    /// Every service calls this (P2a). A service that opts out of the kernel opts out
    /// of being operable — the estate's worked example has exactly one service that
    /// skipped it, and it is precisely the service with no traces when it misbehaves.
    /// </summary>
    public static TBuilder AddServiceDefaults<TBuilder>(this TBuilder builder)
        where TBuilder : IHostApplicationBuilder
    {
        builder.ConfigureOpenTelemetry();
        builder.AddDefaultHealthChecks();

        builder.Services.AddServiceDiscovery();

        builder.Services.ConfigureHttpClientDefaults(http =>
        {
            // Resilience on by default, on every outbound client, with explicit
            // timeouts. Opting in per-client is how one client ends up without it.
            http.AddStandardResilienceHandler();
            http.AddServiceDiscovery();
        });

        return builder;
    }

    /// <summary>
    /// OTLP first (P15). Observability is a build-time decision: the service emits
    /// traces, metrics and logs whether or not anything is listening, and an exporter
    /// is attached only when an endpoint is configured.
    /// </summary>
    public static TBuilder ConfigureOpenTelemetry<TBuilder>(this TBuilder builder)
        where TBuilder : IHostApplicationBuilder
    {
        builder.Logging.AddOpenTelemetry(logging =>
        {
            logging.IncludeFormattedMessage = true;
            logging.IncludeScopes = true;
        });

        builder.Services.AddOpenTelemetry()
            .WithMetrics(metrics =>
            {
                metrics.AddAspNetCoreInstrumentation()
                    .AddHttpClientInstrumentation()
                    .AddRuntimeInstrumentation();
            })
            .WithTracing(tracing =>
            {
                tracing.AddSource(builder.Environment.ApplicationName)
                    .AddAspNetCoreInstrumentation(options =>
                        // Probe noise would otherwise dominate every trace view, and
                        // on a scale-to-zero platform the probes outnumber the users.
                        options.Filter = context =>
                            !context.Request.Path.StartsWithSegments(HealthEndpointPath)
                            && !context.Request.Path.StartsWithSegments(AlivenessEndpointPath))
                    .AddHttpClientInstrumentation();
            });

        builder.AddOpenTelemetryExporters();

        return builder;
    }

    /// <summary>
    /// Optional dependency, degraded rather than required (P8). No endpoint configured
    /// means no exporter — not a startup failure. The spans are still produced, which
    /// is what lets the eval harness read them in-process with nothing configured at all.
    /// </summary>
    private static TBuilder AddOpenTelemetryExporters<TBuilder>(this TBuilder builder)
        where TBuilder : IHostApplicationBuilder
    {
        var useOtlpExporter = !string.IsNullOrWhiteSpace(
            builder.Configuration["OTEL_EXPORTER_OTLP_ENDPOINT"]);

        if (useOtlpExporter)
        {
            builder.Services.AddOpenTelemetry().UseOtlpExporter();
        }

        return builder;
    }

    public static TBuilder AddDefaultHealthChecks<TBuilder>(this TBuilder builder)
        where TBuilder : IHostApplicationBuilder
    {
        builder.Services.AddHealthChecks()
            .AddCheck("self", () => HealthCheckResult.Healthy(), ["live"]);

        return builder;
    }

    /// <summary>
    /// <c>/health</c> is readiness — every check must pass. <c>/alive</c> is liveness —
    /// only <c>live</c>-tagged checks. The platform health check points at
    /// <c>/health</c> with a grace period that covers cold start.
    /// </summary>
    public static WebApplication MapDefaultEndpoints(this WebApplication app)
    {
        app.MapHealthChecks(HealthEndpointPath);

        app.MapHealthChecks(AlivenessEndpointPath, new HealthCheckOptions
        {
            Predicate = r => r.Tags.Contains("live"),
        });

        return app;
    }
}
