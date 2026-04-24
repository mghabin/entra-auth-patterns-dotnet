using System.Security.Claims;
using Ftgo.Auth;
using Microsoft.AspNetCore.Authorization;
using Microsoft.Extensions.Options;
using NSubstitute;
using Shouldly;
using Xunit;

namespace Ftgo.Auth.Tests;

public sealed class RequireClientAppHandlerTests
{
    private static AuthorizationHandlerContext MakeContext(ClaimsPrincipal user, IAuthorizationRequirement req) =>
        new([req], user, resource: null);

    private static IOptionsMonitor<EntraAuthOptions> Monitor(EntraAuthOptions opts)
    {
        var monitor = Substitute.For<IOptionsMonitor<EntraAuthOptions>>();
        monitor.CurrentValue.Returns(opts);
        return monitor;
    }

    private static ClaimsPrincipal App(string azp, params (string type, string value)[] extra)
    {
        var claims = new List<Claim> { new("azp", azp) };
        claims.AddRange(extra.Select(c => new Claim(c.type, c.value)));
        return new ClaimsPrincipal(new ClaimsIdentity(claims, authenticationType: "jwt"));
    }

    [Fact]
    public async Task Rejects_unauthenticated_user()
    {
        var handler = new RequireClientAppHandler(Monitor(new EntraAuthOptions()));
        var ctx = MakeContext(new ClaimsPrincipal(new ClaimsIdentity()), new RequireClientAppRequirement("known-app"));

        await handler.HandleAsync(ctx);

        ctx.HasSucceeded.ShouldBeFalse();
    }

    [Fact]
    public async Task Rejects_user_token_carrying_scp()
    {
        var handler = new RequireClientAppHandler(Monitor(new EntraAuthOptions()));
        var user = App("known-app", ("scp", "User.Read"));
        var ctx = MakeContext(user, new RequireClientAppRequirement("known-app"));

        await handler.HandleAsync(ctx);

        ctx.HasSucceeded.ShouldBeFalse();
    }

    [Fact]
    public async Task Rejects_caller_not_in_allow_list()
    {
        var handler = new RequireClientAppHandler(Monitor(new EntraAuthOptions()));
        var ctx = MakeContext(App("stranger"), new RequireClientAppRequirement("known-app"));

        await handler.HandleAsync(ctx);

        ctx.HasSucceeded.ShouldBeFalse();
    }

    [Fact]
    public async Task Succeeds_when_caller_is_in_explicit_allow_list()
    {
        var handler = new RequireClientAppHandler(Monitor(new EntraAuthOptions()));
        var ctx = MakeContext(App("known-app"), new RequireClientAppRequirement("known-app"));

        await handler.HandleAsync(ctx);

        ctx.HasSucceeded.ShouldBeTrue();
    }

    [Fact]
    public async Task Falls_back_to_configured_allow_list_when_requirement_empty()
    {
        var opts = new EntraAuthOptions { AllowedClientApps = ["configured-app"] };
        var handler = new RequireClientAppHandler(Monitor(opts));
        var ctx = MakeContext(App("configured-app"), new RequireClientAppRequirement());

        await handler.HandleAsync(ctx);

        ctx.HasSucceeded.ShouldBeTrue();
    }

    [Fact]
    public async Task Matches_on_appid_claim_when_azp_missing()
    {
        var handler = new RequireClientAppHandler(Monitor(new EntraAuthOptions()));
        var user = new ClaimsPrincipal(new ClaimsIdentity([new Claim("appid", "v1-app")], "jwt"));
        var ctx = MakeContext(user, new RequireClientAppRequirement("v1-app"));

        await handler.HandleAsync(ctx);

        ctx.HasSucceeded.ShouldBeTrue();
    }
}
