using AbsenceConcierge.AgentService.Extensions;
using AbsenceConcierge.AgentService.Workforce;
using AbsenceConcierge.AgentService.Workforce.Mcp;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace AbsenceConcierge.AgentService.Tests;

/// <summary>
/// Which tool chain the composition root actually builds, per configuration.
///
/// <para>
/// The MCP branch is guarded by "is this deployment credentialed?", and D-11 gave
/// that question a second right answer: a bearer token obtained out of band, or
/// the OAuth flow. These tests pin the decision table — including the row that
/// matters most, which is that a half-configured deployment still degrades to the
/// mock with the demonstrated path unaffected (P8), and the row that keeps forks
/// safe: OAuth "on" with no server to point at is still the mock.
/// </para>
/// </summary>
public sealed class McpCompositionTests
{
    [Fact]
    public void Mcp_mode_with_a_bearer_token_builds_the_mcp_chain()
    {
        var tools = Resolve(new Dictionary<string, string?>
        {
            ["WorkforceTools:Mode"] = "Mcp",
            ["WorkforceTools:Mcp:ServerUrl"] = "https://mcp.example.test/",
            ["WorkforceTools:Mcp:AccessToken"] = "token-from-somewhere-else",
        });

        Assert.IsType<InstrumentedWorkforceTools>(tools);
        Assert.IsType<McpWorkforceTools>(Unwrap(tools));
    }

    [Fact]
    public void Mcp_mode_with_oauth_enabled_and_no_token_builds_the_mcp_chain()
    {
        // D-11's closing shape: the adapter can now ACQUIRE a credential, so a
        // deployment that says "OAuth" and names a server is credentialed enough
        // to compose. Nothing connects at composition time — the session opens on
        // first use — so no flow runs here; what is under test is the decision.
        var tools = Resolve(new Dictionary<string, string?>
        {
            ["WorkforceTools:Mode"] = "Mcp",
            ["WorkforceTools:Mcp:ServerUrl"] = "https://mcp.example.test/",
            ["WorkforceTools:Mcp:OAuth:Enabled"] = "true",
        });

        Assert.IsType<InstrumentedWorkforceTools>(tools);
        Assert.IsType<McpWorkforceTools>(Unwrap(tools));
    }

    [Fact]
    public void Mcp_mode_with_neither_token_nor_oauth_degrades_to_the_mock()
    {
        var tools = Resolve(new Dictionary<string, string?>
        {
            ["WorkforceTools:Mode"] = "Mcp",
            ["WorkforceTools:Mcp:ServerUrl"] = "https://mcp.example.test/",
        });

        Assert.IsType<InstrumentedWorkforceTools>(tools);
        Assert.IsType<MockWorkforceToolsMarker>(MarkerOf(tools));
    }

    [Fact]
    public void Oauth_enabled_without_a_server_url_still_degrades_to_the_mock()
    {
        // The flag alone is not a destination. A fork that copies the flag but no
        // URL must land on the mock, with the demonstrated path unaffected.
        var tools = Resolve(new Dictionary<string, string?>
        {
            ["WorkforceTools:Mode"] = "Mcp",
            ["WorkforceTools:Mcp:OAuth:Enabled"] = "true",
        });

        Assert.IsType<InstrumentedWorkforceTools>(tools);
        Assert.IsType<MockWorkforceToolsMarker>(MarkerOf(tools));
    }

    // ── Helpers ─────────────────────────────────────────────────────────────────

    /// <summary>Stands in for "the mock chain" in assertions below.</summary>
    private sealed record MockWorkforceToolsMarker;

    private static IWorkforceTools Resolve(Dictionary<string, string?> settings)
    {
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(settings).Build();

        var services = new ServiceCollection();
        services.AddLogging(logging => logging.SetMinimumLevel(LogLevel.None));
        services.AddAbsenceConciergeAgent(configuration);
        services.AddWorkforceTools(configuration);

        // The fixture loader would read from disk; the composition decision under
        // test does not need a world until a tool is called, and the mock branch
        // resolves one eagerly — so give it the smallest world that exists.
        services.AddSingleton(TestWorld.Build().World);

        return services.BuildServiceProvider().GetRequiredService<IWorkforceTools>();
    }

    private static object Unwrap(IWorkforceTools tools)
    {
        var field = typeof(InstrumentedWorkforceTools)
            .GetFields(System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)
            .Single(f => typeof(IWorkforceTools).IsAssignableFrom(f.FieldType));

        var inner = field.GetValue(tools)!;

        // The attempt policy decorates between the instrumentation and the
        // implementation; walk through it the same way.
        if (inner is ToolAttemptPolicy)
        {
            var innerField = typeof(ToolAttemptPolicy)
                .GetFields(System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)
                .Single(f => typeof(IWorkforceTools).IsAssignableFrom(f.FieldType));

            return innerField.GetValue(inner)!;
        }

        return inner;
    }

    private static object MarkerOf(IWorkforceTools tools) =>
        Unwrap(tools) is AbsenceConcierge.AgentService.Workforce.Mock.MockWorkforceTools
            ? new MockWorkforceToolsMarker()
            : Unwrap(tools);
}
