using System.Security.Claims;
using Ftgo.Auth;
using Microsoft.AspNetCore.Authorization;
using Microsoft.Extensions.Options;
using NSubstitute;
using Shouldly;
using Xunit;

namespace Ftgo.Auth.Tests;

public sealed class MixedClaimsPolicyHandlerTests
{
    private static AuthorizationHandlerContext MakeContext(ClaimsPrincipal user, params IAuthorizationRequirement[] reqs) =>
        new(reqs, user, resource: null);

    private static ClaimsPrincipal Principal(params (string type, string value)[] claims) =>
        new(new ClaimsIdentity(claims.Select(c => new Claim(c.type, c.value)), authenticationType: "jwt"));

    private static IOptionsMonitor<EntraAuthOptions> Monitor(EntraAuthOptions opts)
    {
        var monitor = Substitute.For<IOptionsMonitor<EntraAuthOptions>>();
        monitor.CurrentValue.Returns(opts);
        return monitor;
    }

    // --- RequireDelegatedScopeHandler ---

    [Fact]
    public async Task DelegatedScope_Succeeds_WhenScpContainsRequiredScope_AndNoRoles()
    {
        var handler = new RequireDelegatedScopeHandler();
        var req = new RequireDelegatedScopeRequirement("orders.read");
        var ctx = MakeContext(Principal(("scp", "orders.read")), req);

        await handler.HandleAsync(ctx);

        ctx.HasSucceeded.ShouldBeTrue();
    }

    [Fact]
    public async Task OrdersDelegated_RejectsTokenWithRoles()
    {
        var handler = new RequireDelegatedScopeHandler();
        var req = new RequireDelegatedScopeRequirement("orders.read");
        var ctx = MakeContext(Principal(("scp", "orders.read"), ("roles", "Orders.Process")), req);

        await handler.HandleAsync(ctx);

        ctx.HasSucceeded.ShouldBeFalse();
        ctx.HasFailed.ShouldBeTrue();
    }

    [Fact]
    public async Task DelegatedScope_Rejects_WhenScpMissing()
    {
        var handler = new RequireDelegatedScopeHandler();
        var ctx = MakeContext(Principal(("oid", Guid.NewGuid().ToString())), new RequireDelegatedScopeRequirement("orders.read"));

        await handler.HandleAsync(ctx);

        ctx.HasFailed.ShouldBeTrue();
    }

    [Fact]
    public async Task DelegatedScope_Rejects_WhenScpDoesNotContainRequired()
    {
        var handler = new RequireDelegatedScopeHandler();
        var ctx = MakeContext(Principal(("scp", "user.read other.scope")), new RequireDelegatedScopeRequirement("orders.read"));

        await handler.HandleAsync(ctx);

        ctx.HasFailed.ShouldBeTrue();
    }

    [Fact]
    public async Task DelegatedScope_Succeeds_WhenScpContainsAnyRequiredScope()
    {
        var handler = new RequireDelegatedScopeHandler();
        var ctx = MakeContext(
            Principal(("scp", "orders.read other.scope")),
            new RequireDelegatedScopeRequirement("orders.write", "orders.read"));

        await handler.HandleAsync(ctx);

        ctx.HasSucceeded.ShouldBeTrue();
    }

    [Fact]
    public async Task DelegatedScope_Rejects_Unauthenticated()
    {
        var handler = new RequireDelegatedScopeHandler();
        var ctx = MakeContext(new ClaimsPrincipal(new ClaimsIdentity()), new RequireDelegatedScopeRequirement("orders.read"));

        await handler.HandleAsync(ctx);

        ctx.HasSucceeded.ShouldBeFalse();
    }

    // --- RequireAppRoleHandler ---

    [Fact]
    public async Task AppRole_Succeeds_WhenRolesContainsRequired_AndNoScp()
    {
        var handler = new RequireAppRoleHandler();
        var ctx = MakeContext(Principal(("roles", "Orders.Process")), new RequireAppRoleRequirement("Orders.Process"));

        await handler.HandleAsync(ctx);

        ctx.HasSucceeded.ShouldBeTrue();
    }

    [Fact]
    public async Task OrdersApp_RejectsTokenWithScp()
    {
        var handler = new RequireAppRoleHandler();
        var ctx = MakeContext(
            Principal(("roles", "Orders.Process"), ("scp", "orders.read")),
            new RequireAppRoleRequirement("Orders.Process"));

        await handler.HandleAsync(ctx);

        ctx.HasSucceeded.ShouldBeFalse();
        ctx.HasFailed.ShouldBeTrue();
    }

    [Fact]
    public async Task AppRole_Rejects_WhenRolesMissing()
    {
        var handler = new RequireAppRoleHandler();
        var ctx = MakeContext(Principal(("azp", "x")), new RequireAppRoleRequirement("Orders.Process"));

        await handler.HandleAsync(ctx);

        ctx.HasFailed.ShouldBeTrue();
    }

    [Fact]
    public async Task AppRole_Rejects_WhenRolesValueDoesNotMatch()
    {
        var handler = new RequireAppRoleHandler();
        var ctx = MakeContext(Principal(("roles", "Other.Role")), new RequireAppRoleRequirement("Orders.Process"));

        await handler.HandleAsync(ctx);

        ctx.HasFailed.ShouldBeTrue();
    }

    [Fact]
    public void RequireDelegatedScopeRequirement_Throws_WhenNoScopes()
    {
        Should.Throw<ArgumentException>(() => new RequireDelegatedScopeRequirement());
    }

    [Fact]
    public void RequireAppRoleRequirement_Throws_WhenRoleBlank()
    {
        Should.Throw<ArgumentException>(() => new RequireAppRoleRequirement("  "));
    }
}
