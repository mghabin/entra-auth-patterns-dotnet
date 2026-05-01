using System.Net;
using System.Security.Claims;
using Ftgo.Auth;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Shouldly;
using Xunit;

namespace Ftgo.Auth.Tests;

public sealed class EntraAuthRateLimiterTests
{
    [Fact]
    public async Task Default_policy_allows_requests_under_limit()
    {
        using var host = await BuildHostAsync(permitLimit: 5);
        using var client = host.GetTestClient();

        for (var i = 0; i < 5; i++)
        {
            var response = await client.GetAsync(new Uri("/", UriKind.Relative), TestContext.Current.CancellationToken);
            response.StatusCode.ShouldBe(HttpStatusCode.OK);
        }
    }

    [Fact]
    public async Task Default_policy_returns_429_with_retry_after_when_over_limit()
    {
        using var host = await BuildHostAsync(permitLimit: 2);
        using var client = host.GetTestClient();

        await client.GetAsync(new Uri("/", UriKind.Relative), TestContext.Current.CancellationToken);
        await client.GetAsync(new Uri("/", UriKind.Relative), TestContext.Current.CancellationToken);
        var rejected = await client.GetAsync(new Uri("/", UriKind.Relative), TestContext.Current.CancellationToken);

        rejected.StatusCode.ShouldBe(HttpStatusCode.TooManyRequests);
    }

    [Fact]
    public void Partition_key_uses_tid_and_oid_when_authenticated()
    {
        var ctx = new DefaultHttpContext();
        ctx.User = new ClaimsPrincipal(new ClaimsIdentity(new[]
        {
            new Claim("tid", "tenant-1"),
            new Claim("oid", "user-1"),
        }, authenticationType: "test"));

        var key = EntraAuthRateLimiterExtensions.PartitionKey(ctx);

        key.ShouldBe("u|tenant-1|user-1");
    }

    [Fact]
    public void Partition_key_uses_remote_ip_when_unauthenticated()
    {
        var ctx = new DefaultHttpContext();
        ctx.Connection.RemoteIpAddress = System.Net.IPAddress.Parse("203.0.113.7");

        var key = EntraAuthRateLimiterExtensions.PartitionKey(ctx);

        key.ShouldBe("ip|203.0.113.7");
    }

    [Fact]
    public void Partition_key_buckets_app_only_token_separately_from_user_token()
    {
        // App-only token: idtyp=app, no scp, has roles, has azp/appid.
        var appCtx = new DefaultHttpContext();
        appCtx.User = new ClaimsPrincipal(new ClaimsIdentity(new[]
        {
            new Claim("tid", "tenant-1"),
            new Claim("idtyp", "app"),
            new Claim("azp", "service-app-1"),
            new Claim("roles", "Orders.Process"),
        }, authenticationType: "test"));

        var appResult = EntraAuthRateLimiterExtensions.PartitionKeyAndKind(appCtx);
        appResult.Key.ShouldBe("a|tenant-1|service-app-1");
        appResult.IsApp.ShouldBeTrue();
    }

    [Fact]
    public void Partition_key_treats_roles_only_token_as_app_even_without_idtyp()
    {
        // Older tokens without idtyp: presence of `roles` and absence of `scp` => app-only.
        var ctx = new DefaultHttpContext();
        ctx.User = new ClaimsPrincipal(new ClaimsIdentity(new[]
        {
            new Claim("tid", "tenant-2"),
            new Claim("appid", "legacy-app"),
            new Claim("roles", "Restaurants.Read"),
        }, authenticationType: "test"));

        var (key, isApp) = EntraAuthRateLimiterExtensions.PartitionKeyAndKind(ctx);
        key.ShouldBe("a|tenant-2|legacy-app");
        isApp.ShouldBeTrue();
    }

    private static async Task<IHost> BuildHostAsync(int permitLimit)
    {
        var builder = new HostBuilder().ConfigureWebHost(web =>
        {
            web.UseTestServer();
            web.ConfigureServices(services =>
            {
                services.AddRouting();
                services.AddEntraAuthRateLimiter(o =>
                {
                    o.PermitLimit = permitLimit;
                    o.Window = TimeSpan.FromSeconds(60);
                });
            });
            web.Configure(app =>
            {
                app.UseRouting();
                app.UseRateLimiter();
                app.UseEndpoints(e =>
                {
                    e.MapGet("/", (HttpContext _) => Results.Ok("ok"))
                        .RequireRateLimiting(EntraAuthRateLimiterExtensions.DefaultPolicyName);
                });
            });
        });
        var host = await builder.StartAsync(TestContext.Current.CancellationToken);
        return host;
    }
}
