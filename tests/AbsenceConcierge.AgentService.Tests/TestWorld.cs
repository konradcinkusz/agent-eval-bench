using AbsenceConcierge.AgentService.Workforce.Confirmation;
using AbsenceConcierge.AgentService.Workforce.Fixtures;
using AbsenceConcierge.AgentService.Workforce.Mock;
using Microsoft.Extensions.Logging.Abstractions;

namespace AbsenceConcierge.AgentService.Tests;

/// <summary>
/// A clock that does not move. Every test that touches a date pins one, for the same
/// reason every eval scenario does: a test that passes only in August is not a test.
/// </summary>
public sealed class FixedTimeProvider(DateTimeOffset now) : TimeProvider
{
    public override DateTimeOffset GetUtcNow() => now;
}

/// <summary>
/// Builds the system under test from the real fixture file — the same one the service
/// loads and the same one the eval scenarios name.
///
/// Deliberately not a hand-built in-memory world: a parallel fake would let these
/// tests pass against a shape the deployed service does not have, which is the seam
/// the estate's "mock modes are test seams, build the seam into the service" rule
/// exists to close.
/// </summary>
public static class TestWorld
{
    public const string ActorEmployeeId = "emp-001";
    public const string VacationTypeId = "lt-201";
    public const string SickTypeId = "lt-202";

    /// <summary>A Tuesday, matching the reference scenario's clock.</summary>
    public static readonly DateTimeOffset Now = new(2026, 8, 11, 9, 15, 0, TimeSpan.FromHours(2));

    public static string FixtureDirectory => Path.Combine(AppContext.BaseDirectory, "fixtures");

    public static WorkforceWorld Load(string name = "meridian-labs") =>
        new FixtureLoader(NullLogger<FixtureLoader>.Instance, FixtureDirectory).Load(name);

    public static (MockWorkforceTools Tools, IConfirmationTokenStore Tokens, WorkforceWorld World) Build(
        WorkforceWorld? world = null)
    {
        world ??= Load();
        var tokens = new InMemoryConfirmationTokenStore();
        var tools = new MockWorkforceTools(world, tokens, new FixedTimeProvider(Now));
        return (tools, tokens, world);
    }

    /// <summary>The world with the actor's permissions replaced — for denied-path tests.</summary>
    public static WorkforceWorld WithPermissions(params string[] permissions)
    {
        var world = Load();
        return world with { Actor = world.Actor with { Permissions = permissions } };
    }

    /// <summary>Issues a token and approves it, as a completed confirmation gate would.</summary>
    public static string ApprovedToken(
        IConfirmationTokenStore tokens,
        string leaveTypeId,
        DateOnly start,
        DateOnly end)
    {
        var token = tokens.Issue(new ConfirmationDraft(ActorEmployeeId, leaveTypeId, start, end));
        tokens.Approve(token);
        return token;
    }
}
