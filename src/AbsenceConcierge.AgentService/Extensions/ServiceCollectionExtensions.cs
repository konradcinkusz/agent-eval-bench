using AbsenceConcierge.AgentService.Agent;
using AbsenceConcierge.AgentService.Agent.Language;
using AbsenceConcierge.AgentService.Agent.Llm;
using AbsenceConcierge.AgentService.Agent.Steps;
using AbsenceConcierge.AgentService.Telemetry;
using AbsenceConcierge.AgentService.Workforce;
using AbsenceConcierge.AgentService.Workforce.Confirmation;
using AbsenceConcierge.AgentService.Workforce.Fixtures;
using AbsenceConcierge.AgentService.Workforce.Mock;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Options;

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
            // trace shape because every implementation goes through this wrapper —
            // including the attempt loop, which must live inside the span so that
            // one logical call stays one span (SPEC §2.2.1).
            var attempts = new ToolAttemptPolicy(
                sp.GetRequiredService<IOptions<AgentOptions>>().Value.MaxReadAttempts);

            return new InstrumentedWorkforceTools(tools, attempts);
        });

        return services;
    }

    /// <summary>
    /// Registers the agent: its options, its two language seams, its conversation
    /// state, and the step pipeline.
    ///
    /// <para>
    /// <b>The pipeline's order is the specification</b>, and this is where it is
    /// written down. It is a reviewable list rather than a model deciding what to do
    /// next, which is what makes every constraint in SPEC §4 a property of the code
    /// instead of a hope about a prompt. Adding a behaviour means adding a class and
    /// a line here (P10).
    /// </para>
    /// </summary>
    public static IServiceCollection AddAbsenceConciergeAgent(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        services.Configure<AgentOptions>(configuration.GetSection(AgentOptions.SectionName));
        services.Configure<LlmOptions>(configuration.GetSection(LlmOptions.SectionName));

        // The default path has no model, no credential and no network (ADR-0002).
        // The model-backed implementations register over these when one is configured.
        services.TryAddSingleton<IUtteranceInterpreter, DeterministicUtteranceInterpreter>();
        services.TryAddSingleton<IReplyComposer, DeterministicReplyComposer>();

        services.TryAddSingleton<IAgentConversationStore, InMemoryAgentConversationStore>();

        services.AddSingleton<IAgentStep, EstablishActorStep>();
        services.AddSingleton<IAgentStep, ConfirmationDecisionStep>();
        services.AddSingleton<IAgentStep, InterpretUtteranceStep>();
        services.AddSingleton<IAgentStep, ScopeGuardStep>();
        services.AddSingleton<IAgentStep, ResolvePersonStep>();
        services.AddSingleton<IAgentStep, ResolveDatesStep>();
        services.AddSingleton<IAgentStep, LeaveTypeStep>();
        services.AddSingleton<IAgentStep, ConflictCheckStep>();
        services.AddSingleton<IAgentStep, DraftStep>();
        services.AddSingleton<IAgentStep, ConfirmationGateStep>();
        services.AddSingleton<IAgentStep, ExecuteWriteStep>();

        services.AddSingleton<IAgentOrchestrator, AgentOrchestrator>();

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
