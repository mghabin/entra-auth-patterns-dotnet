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

    [Theory]
    [InlineData("tid", "oid", "u|tenant-x|user-y")]
    [InlineData("http://schemas.microsoft.com/identity/claims/tenantid", "http://schemas.microsoft.com/identity/claims/objectidentifier", "u|tenant-x|user-y")]
    [InlineData("tid", "sub", "u|tenant-x|user-y")]
    public void Partition_key_resolves_long_form_user_claim_variants(string tidClaim, string oidClaim, string expected)
    {
        var ctx = new DefaultHttpContext();
        ctx.User = new ClaimsPrincipal(new ClaimsIdentity(new[]
        {
            new Claim(tidClaim, "tenant-x"),
            new Claim(oidClaim, "user-y"),
        }, authenticationType: "test"));

        EntraAuthRateLimiterExtensions.PartitionKey(ctx).ShouldBe(expected);
    }

    [Theory]
    [InlineData("azp", "client-x")]
    [InlineData("appid", "client-x")]
    [InlineData("http://schemas.microsoft.com/identity/claims/appid", "client-x")]
    public void Partition_key_resolves_app_appid_claim_variants(string claimType, string value)
    {
        var ctx = new DefaultHttpContext();
        ctx.User = new ClaimsPrincipal(new ClaimsIdentity(new[]
        {
            new Claim("tid", "tenant-x"),
            new Claim("idtyp", "app"),
            new Claim(claimType, value),
            new Claim("roles", "Orders.Process"),
        }, authenticationType: "test"));

        EntraAuthRateLimiterExtensions.PartitionKey(ctx).ShouldBe($"a|tenant-x|{value}");
    }

    [Fact]
    public void Partition_key_falls_back_to_unknown_when_claims_missing_but_authenticated()
    {
        var ctx = new DefaultHttpContext();
        ctx.User = new ClaimsPrincipal(new ClaimsIdentity(authenticationType: "test"));

        var key = EntraAuthRateLimiterExtensions.PartitionKey(ctx);

        key.ShouldStartWith("u|unknown-tid|");
        key.ShouldEndWith("|unknown-oid");
    }

    [Fact]
    public async Task Default_policy_isolates_partitions_across_distinct_users()
    {
        using var host = await BuildHostAsync(permitLimit: 2);
        using var client = host.GetTestClient();

        // User A exhausts their budget.
        await SendAsAsync(client, tid: "tenant-a", oid: "user-a");
        await SendAsAsync(client, tid: "tenant-a", oid: "user-a");
        var aRejected = await SendAsAsync(client, tid: "tenant-a", oid: "user-a");

        // User B in same tenant is unaffected.
        var bAllowed = await SendAsAsync(client, tid: "tenant-a", oid: "user-b");

        aRejected.StatusCode.ShouldBe(HttpStatusCode.TooManyRequests);
        bAllowed.StatusCode.ShouldBe(HttpStatusCode.OK);
    }

    [Fact]
    public async Task Default_policy_emits_RetryAfter_header_on_429()
    {
        using var host = await BuildHostAsync(permitLimit: 1);
        using var client = host.GetTestClient();

        await SendAsAsync(client, tid: "tenant-r", oid: "user-r");
        var rejected = await SendAsAsync(client, tid: "tenant-r", oid: "user-r");

        rejected.StatusCode.ShouldBe(HttpStatusCode.TooManyRequests);
        // RFC 6585 §3 requires Retry-After on 429. The OnRejected hook in
        // EntraAuthRateLimiterExtensions writes it from the lease metadata.
        rejected.Headers.RetryAfter.ShouldNotBeNull();
        rejected.Headers.RetryAfter!.Delta.ShouldNotBeNull();
        rejected.Headers.RetryAfter.Delta!.Value.ShouldBeGreaterThan(TimeSpan.Zero);
    }

    private static Task<HttpResponseMessage> SendAsAsync(HttpClient client, string tid, string oid)
    {
        var req = new HttpRequestMessage(HttpMethod.Get, "/");
        req.Headers.Add("X-Test-Tid", tid);
        req.Headers.Add("X-Test-Oid", oid);
        return client.SendAsync(req, TestContext.Current.CancellationToken);
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
                // Synthesize a ClaimsPrincipal from headers so partition-isolation tests can
                // exercise multiple identities through one HttpClient.
                app.Use(async (ctx, next) =>
                {
                    if (ctx.Request.Headers.TryGetValue("X-Test-Tid", out var tid) &&
                        ctx.Request.Headers.TryGetValue("X-Test-Oid", out var oid))
                    {
                        ctx.User = new ClaimsPrincipal(new ClaimsIdentity(new[]
                        {
                            new Claim("tid", tid!),
                            new Claim("oid", oid!),
                        }, authenticationType: "test"));
                    }
                    await next();
                });

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
