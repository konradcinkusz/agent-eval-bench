using AbsenceConcierge.AgentService.Telemetry;
using AbsenceConcierge.AgentService.Workforce;
using AbsenceConcierge.AgentService.Workforce.Confirmation;
using AbsenceConcierge.AgentService.Workforce.Fixtures;
using AbsenceConcierge.AgentService.Workforce.Mock;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Options;
using OpenTelemetry.Trace;

namespace AbsenceConcierge.AgentService.Extensions;

public sealed class WorkforceToolsOptions
{
    public const string SectionName = "WorkforceTools";

    /// <summary>Mock (default) | Mcp.</summary>
    public string Mode { get; set; } = WorkforceToolsMode.Mock;

    /// <summary>Fixture file name, without extension, under <c>fixtures/</c>.</summary>
    public string Fixture { get; set; } = "meridian-labs";
}

public static class WorkforceToolsMode
{
    public const string Mock = "Mock";
    public const string Mcp = "Mcp";
}

/// <summary>
/// Wiring. Program.cs reads as a list of capabilities; the configuration lives here
/// (P9), one call per capability.
/// </summary>
public static class ServiceCollectionExtensions
{
    /// <summary>
    /// Registers the workforce tool surface, decorated with the tracing that the eval
    /// harness reads.
    /// </summary>
    public static IServiceCollection AddWorkforceTools(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        services.Configure<WorkforceToolsOptions>(configuration.GetSection(WorkforceToolsOptions.SectionName));

        services.TryAddSingleton(TimeProvider.System);
        services.AddSingleton<IConfirmationTokenStore, InMemoryConfirmationTokenStore>();

        services.AddSingleton<IFixtureLoader>(sp => new FixtureLoader(
            sp.GetRequiredService<ILogger<FixtureLoader>>(),
            Path.Combine(AppContext.BaseDirectory, "fixtures")));

        services.AddSingleton(sp =>
        {
            var options = sp.GetRequiredService<IOptions<WorkforceToolsOptions>>().Value;
            return sp.GetRequiredService<IFixtureLoader>().Load(options.Fixture);
        });

        services.AddSingleton<IWorkforceTools>(sp =>
        {
            var options = sp.GetRequiredService<IOptions<WorkforceToolsOptions>>().Value;
            var logger = sp.GetRequiredService<ILogger<MockWorkforceTools>>();

            // P8: an optional integration that is not configured degrades to the
            // working fallback with a log line naming what was missing. It does not
            // fail startup, and it does not pretend to be the thing it is not.
            if (string.Equals(options.Mode, WorkforceToolsMode.Mcp, StringComparison.OrdinalIgnoreCase))
            {
                logger.LogWarning(
                    "WorkforceTools:Mode is 'Mcp', but the MCP adapter is not implemented until Phase 7. "
                    + "Falling back to Mock. The demonstrated path is unaffected.");
            }

            IWorkforceTools tools = new MockWorkforceTools(
                sp.GetRequiredService<WorkforceWorld>(),
                sp.GetRequiredService<IConfirmationTokenStore>(),
                sp.GetRequiredService<TimeProvider>());

            // Decoration, not inheritance (P10). Every implementation gets the same
            // trace shape because every implementation goes through this wrapper.
            return new InstrumentedWorkforceTools(tools);
        });

        return services;
    }

    /// <summary>
    /// Registers the agent's own ActivitySource with the tracer provider the kernel
    /// configured. The kernel knows nothing about this source — it is domain
    /// vocabulary, and the kernel holds no domain (P2).
    /// </summary>
    public static IServiceCollection AddAgentTelemetry(this IServiceCollection services)
    {
        services.AddOpenTelemetry()
            .WithTracing(tracing => tracing.AddSource(AgentDiagnostics.ActivitySourceName));

        return services;
    }
}
