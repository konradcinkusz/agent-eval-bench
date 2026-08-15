using System.Threading.RateLimiting;
using AbsenceConcierge.AgentService.Agent;
using AbsenceConcierge.AgentService.Agent.Language;
using AbsenceConcierge.AgentService.Agent.Llm;
using AbsenceConcierge.AgentService.Agent.Steps;
using AbsenceConcierge.AgentService.Demo;
using AbsenceConcierge.AgentService.Telemetry;
using AbsenceConcierge.AgentService.Workforce;
using AbsenceConcierge.AgentService.Workforce.Confirmation;
using AbsenceConcierge.AgentService.Workforce.Fixtures;
using AbsenceConcierge.AgentService.Workforce.Mcp;
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
        services.Configure<McpOptions>(configuration.GetSection(McpOptions.SectionName));

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

        // Resolved only on the MCP branch below, which is what keeps the constructor —
        // and the credential it needs — out of the default path entirely. The container
        // owns the session, so it is the container that closes it at shutdown.
        services.AddSingleton<IMcpToolSession>(sp =>
            new McpClientSession(sp.GetRequiredService<IOptions<McpOptions>>().Value));

        services.AddSingleton<IWorkforceTools>(sp =>
        {
            var options = sp.GetRequiredService<IOptions<WorkforceToolsOptions>>().Value;
            var mcp = sp.GetRequiredService<IOptions<McpOptions>>().Value;
            var maxReadAttempts = sp.GetRequiredService<IOptions<AgentOptions>>().Value.MaxReadAttempts;
            var logger = sp.GetRequiredService<ILogger<MockWorkforceTools>>();

            if (string.Equals(options.Mode, WorkforceToolsMode.Mcp, StringComparison.OrdinalIgnoreCase))
            {
                // P8: an optional integration whose configuration is absent degrades to
                // the working fallback with a log line naming what was missing. It does
                // not fail startup, and it does not half-configure a client and try
                // anyway. This is also the deployment control — the public deployment
                // carries no MCP settings at all, so this branch is unreachable there
                // rather than merely switched off (ADR-0005).
                var missing = MissingMcpSettings(mcp);

                if (missing.Count > 0)
                {
                    logger.LogWarning(
                        "WorkforceTools:Mode is 'Mcp' but {Missing} is not configured. Falling back to Mock; "
                        + "the demonstrated path is unaffected.",
                        string.Join(" and ", missing));
                }
                else
                {
                    // Same instrumentation, same attempt policy, same span shape as the
                    // mock — one assembly order, in one factory.
                    return WorkforceToolsFactory.Instrument(
                        new McpWorkforceTools(
                            sp.GetRequiredService<IMcpToolSession>(),
                            mcp,
                            sp.GetRequiredService<IConfirmationTokenStore>(),
                            sp.GetRequiredService<ILogger<McpWorkforceTools>>()),
                        maxReadAttempts);
                }
            }

            // Decoration, not inheritance (P10). Every implementation gets the same
            // trace shape because every implementation goes through the same
            // factory — the one the eval harness also calls, so the suite cannot
            // measure a chain the service does not have.
            return WorkforceToolsFactory.Build(
                sp.GetRequiredService<WorkforceWorld>(),
                sp.GetRequiredService<IConfirmationTokenStore>(),
                sp.GetRequiredService<TimeProvider>(),
                maxReadAttempts);
        });

        return services;
    }

    /// <summary>
    /// Which MCP settings are absent, by name, so the log line says what to set rather
    /// than that something is wrong. Two ways to be credentialed — a bearer token
    /// obtained out of band, or the OAuth flow (D-11) — and either satisfies the
    /// check; the log line names both so the reader picks one rather than hunting.
    /// </summary>
    private static List<string> MissingMcpSettings(McpOptions options)
    {
        List<string> missing = [];

        if (string.IsNullOrWhiteSpace(options.ServerUrl))
        {
            missing.Add($"{McpOptions.SectionName}:ServerUrl");
        }

        if (string.IsNullOrWhiteSpace(options.AccessToken) && !options.OAuth.Enabled)
        {
            missing.Add($"{McpOptions.SectionName}:AccessToken (or {McpOptions.SectionName}:OAuth:Enabled)");
        }

        return missing;
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

        // A pinned clock, from configuration, for the browser suite and nothing
        // else. The eval harness pins time by construction (ScenarioRunner); the
        // Playwright suite drives the REAL service over HTTP, and "I'm sick today
        // and probably tomorrow" typed on a Saturday resolves onto a weekend — a
        // suite green Monday-to-Friday is not a suite (SPEC §9). Guarded the same
        // way every optional setting is: absent means the system clock, and a
        // deployment that sets it gets a log line loud enough to notice, because a
        // demo frozen in time is otherwise a very quiet bug.
        if (DateTimeOffset.TryParse(
                configuration["Agent:PinnedUtcNow"],
                System.Globalization.CultureInfo.InvariantCulture,
                System.Globalization.DateTimeStyles.RoundtripKind,
                out var pinned))
        {
            services.AddSingleton<TimeProvider>(sp =>
            {
                sp.GetRequiredService<ILogger<AgentOrchestrator>>().LogWarning(
                    "Agent:PinnedUtcNow is set: the clock is FROZEN at {Pinned}. This exists for the "
                    + "end-to-end suite; no deployed environment should set it.",
                    pinned);

                return new PinnedTimeProvider(pinned);
            });
        }

        // The default path has no model, no credential and no network (ADR-0002).
        // The model-backed implementations register over these when one is configured.
        services.TryAddSingleton<IUtteranceInterpreter, DeterministicUtteranceInterpreter>();

        // Registered concretely as well as behind the interface, because the live
        // composer takes it as its fallback. A model-backed composer that fell back
        // to "whatever IReplyComposer resolves to" would resolve to itself.
        services.TryAddSingleton<DeterministicReplyComposer>();
        services.TryAddSingleton<IReplyComposer>(sp => sp.GetRequiredService<DeterministicReplyComposer>());

        services.TryAddSingleton<IPromptLibrary>(_ => new PromptLibrary(
            Path.Combine(AppContext.BaseDirectory, "prompts")));

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
    /// Registers the public demo's ceilings, and the live composer if — and only
    /// if — a model is configured.
    ///
    /// <para>
    /// Three separate conditions have to hold before a stranger's request can spend
    /// money: a provider configured <em>and</em> credentialed, an access code set
    /// from a secret, and budget left today. Each is checked in a different place and
    /// each fails closed. This method is the first of the three, and it is the one
    /// that decides whether the code path exists at all — on the public deployment
    /// with no <c>Llm__ApiKey</c>, the live composer is never constructed.
    /// </para>
    /// </summary>
    public static IServiceCollection AddDemoMode(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        services.Configure<DemoOptions>(configuration.GetSection(DemoOptions.SectionName));

        services.TryAddSingleton(TimeProvider.System);
        services.AddSingleton<IDemoBudget, DemoBudget>();
        services.AddSingleton<IDemoClientQuota, DemoClientQuota>();

        // The body ceiling, applied at the server rather than per route: the routes
        // accept three short strings, the framework default is thirty megabytes, and
        // partial coverage is the recorded failure mode (SECURITY-REVIEW.md §9).
        services.Configure<Microsoft.AspNetCore.Server.Kestrel.Core.KestrelServerOptions>(kestrel =>
            kestrel.Limits.MaxRequestBodySize = Math.Max(
                1024,
                configuration.GetSection(DemoOptions.SectionName)
                    .GetValue("MaxRequestBodyBytes", new DemoOptions().MaxRequestBodyBytes)));

        // Resilience from the kernel, not hand-rolled here (P2a). The named client
        // exists so the provider's timeouts and retries are the estate's, and so a
        // slow model cannot hold a request open indefinitely.
        services.AddHttpClient(LlmHttpClientName);

        services.AddSingleton(sp =>
        {
            var options = sp.GetRequiredService<IOptions<LlmOptions>>().Value;
            var factory = sp.GetRequiredService<IHttpClientFactory>();
            var logger = sp.GetRequiredService<ILogger<AgentOrchestrator>>();

            // Null when unconfigured, and that is the ordinary case rather than a
            // fault (P8). It throws only for a provider that was named and never
            // written, which must not be reported as a missing credential.
            var provider = LlmProviderFactory.Create(options, factory.CreateClient(LlmHttpClientName));

            if (provider is null)
            {
                logger.LogInformation(
                    "No language model is configured. Replies are composed deterministically and the demo's "
                    + "live mode is unavailable.");
            }

            return new LlmProviderHandle(provider);
        });

        services.AddSingleton(sp => new DemoAccess(
            sp.GetRequiredService<IOptions<DemoOptions>>(),
            sp.GetRequiredService<IDemoBudget>(),
            sp.GetRequiredService<IDemoClientQuota>(),
            sp.GetRequiredService<LlmProviderHandle>().Provider));

        services.AddSingleton<IReplyComposer>(sp =>
        {
            if (sp.GetRequiredService<LlmProviderHandle>().Provider is not { } provider)
            {
                return sp.GetRequiredService<DeterministicReplyComposer>();
            }

            // Registered after AddAbsenceConciergeAgent's TryAdd, so this is the
            // resolved one. Explicit rather than clever: the agent registers the
            // default it can always satisfy, and the demo replaces it only when the
            // thing it needs is actually present.
            return new ModelBackedReplyComposer(
                sp.GetRequiredService<DeterministicReplyComposer>(),
                provider,
                sp.GetRequiredService<IDemoBudget>(),
                sp.GetRequiredService<IPromptLibrary>(),
                sp.GetRequiredService<IOptions<DemoOptions>>(),
                sp.GetRequiredService<ILogger<ModelBackedReplyComposer>>());
        });

        return services;
    }

    /// <summary>
    /// The name the demo's rate-limit policy is applied by.
    /// </summary>
    public const string DemoRateLimitPolicy = "demo";

    /// <summary>
    /// One rate limit, covering every route a stranger can reach.
    ///
    /// <para>
    /// SECURITY-REVIEW.md §9 names partial coverage as <em>the</em> normal failure
    /// mode — "rate limiting present in most services … and the unprotected one is
    /// the target". So this is applied to the whole agent surface rather than to the
    /// expensive route, and the health endpoints are what remain outside it, because
    /// a probe that gets 429'd takes the machine down.
    /// </para>
    /// <para>
    /// Partitioned by client IP, which SERVICE-API-PATTERNS.md §1 is explicit is the
    /// weaker of its two keys — one office behind one NAT shares a bucket. The
    /// stronger key is an authenticated user id and this demo deliberately has no
    /// accounts, so the collapse is accepted rather than solved, and the limit is set
    /// high enough that a shared bucket is still generous for a page with one button.
    /// The real spend control is the token budget, which no amount of IP rotation
    /// moves.
    /// </para>
    /// </summary>
    public static IServiceCollection AddDemoRateLimiting(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        var demo = configuration.GetSection(DemoOptions.SectionName);
        var perMinute = Math.Max(1, demo.GetValue("RequestsPerMinutePerClient", 20));
        var concurrent = Math.Max(1, demo.GetValue("MaxConcurrentRequests", new DemoOptions().MaxConcurrentRequests));

        services.AddRateLimiter(limiter =>
        {
            limiter.RejectionStatusCode = StatusCodes.Status429TooManyRequests;

            // Partitioned by the SAME client key the live allowance uses. Behind
            // Fly's proxy the socket peer is the proxy, and a limiter keyed on it
            // would put every visitor on the internet into one bucket.
            limiter.AddPolicy(DemoRateLimitPolicy, http => RateLimitPartition.GetFixedWindowLimiter(
                DemoClientKey.Resolve(http),
                _ => new FixedWindowRateLimiterOptions
                {
                    PermitLimit = perMinute,
                    Window = TimeSpan.FromMinutes(1),

                    // No queue. A queued request on a scale-to-zero machine holds a
                    // connection open to tell somebody "no" more politely later, and
                    // the honest answer to a rate limit is immediate.
                    QueueLimit = 0,
                }));

            // A process-wide in-flight ceiling under the per-client window. The
            // window bounds one client's rate; this bounds what every client
            // together can hold open on a 512 MB machine at once. Health probes are
            // exempt — a probe that gets 429'd takes the machine down, which is the
            // one outcome worse than the burst.
            limiter.GlobalLimiter = PartitionedRateLimiter.Create<HttpContext, string>(http =>
                http.Request.Path.StartsWithSegments("/health")
                || http.Request.Path.StartsWithSegments("/alive")
                    ? RateLimitPartition.GetNoLimiter("probes")
                    : RateLimitPartition.GetConcurrencyLimiter("everything-else", _ => new ConcurrencyLimiterOptions
                    {
                        PermitLimit = concurrent,
                        QueueLimit = 0,
                    }));
        });

        return services;
    }

    /// <summary>
    /// A nullable service, held in a non-nullable box.
    ///
    /// <para>
    /// The container cannot register "maybe an <c>ILlmProvider</c>", and
    /// <c>GetService&lt;T&gt;()</c> returning null would make "not configured"
    /// indistinguishable from "somebody forgot to call AddDemoMode". The box is
    /// always registered; what it holds is the decision.
    /// </para>
    /// </summary>
    public sealed record LlmProviderHandle(ILlmProvider? Provider);

    /// <summary>A clock that does not move. Registered only when <c>Agent:PinnedUtcNow</c> is set.</summary>
    private sealed class PinnedTimeProvider(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }

    private const string LlmHttpClientName = "llm";

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
