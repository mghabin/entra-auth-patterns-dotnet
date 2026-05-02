using Ftgo.Auth;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using Shouldly;
using Xunit;

namespace Ftgo.Auth.Tests;

public sealed class EntraAuthForwardedHeadersExtensionsTests
{
    [Fact]
    public void Honours_XForwardedFor_Proto_And_Host()
    {
        var options = ConfigureAndResolve();

        options.ForwardedHeaders.HasFlag(ForwardedHeaders.XForwardedFor).ShouldBeTrue();
        options.ForwardedHeaders.HasFlag(ForwardedHeaders.XForwardedProto).ShouldBeTrue();
        options.ForwardedHeaders.HasFlag(ForwardedHeaders.XForwardedHost).ShouldBeTrue();
    }

    [Fact]
    public void Caps_ForwardLimit_To_2_To_Limit_TunnelDepth()
    {
        var options = ConfigureAndResolve();
        options.ForwardLimit.ShouldBe(2);
    }

    [Fact]
    public async Task Middleware_Updates_RemoteIp_From_XForwardedFor()
    {
        using var host = await BuildHost();
        var client = host.GetTestClient();

        var req = new HttpRequestMessage(HttpMethod.Get, "/probe");
        req.Headers.Add("X-Forwarded-For", "203.0.113.42");
        req.Headers.Add("X-Forwarded-Proto", "https");
        req.Headers.Add("X-Forwarded-Host", "api.contoso.com");

        var response = await client.SendAsync(req, TestContext.Current.CancellationToken);

        var body = await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);
        body.ShouldContain("ip=203.0.113.42");
        body.ShouldContain("scheme=https");
        body.ShouldContain("host=api.contoso.com");
    }

    [Fact]
    public async Task Middleware_Honours_ForwardLimit_When_Chain_Too_Long()
    {
        using var host = await BuildHost();
        var client = host.GetTestClient();

        var req = new HttpRequestMessage(HttpMethod.Get, "/probe");
        // 3 entries; ForwardLimit=2 means the middleware processes only the last 2 (rightmost).
        // The resulting RemoteIp is the deepest *trusted* hop, which is the 2nd-from-right.
        req.Headers.Add("X-Forwarded-For", "198.51.100.1, 198.51.100.2, 203.0.113.42");

        var response = await client.SendAsync(req, TestContext.Current.CancellationToken);

        var body = await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);
        // 198.51.100.1 (the first / least-trusted entry) MUST NOT win — that would be the bug
        // ForwardLimit guards against. Either of the other two is acceptable per ForwardedHeaders semantics.
        body.ShouldNotContain("ip=198.51.100.1");
    }

    private static ForwardedHeadersOptions ConfigureAndResolve()
    {
        var services = new ServiceCollection();
        services.AddEntraAuthForwardedHeaders();
        var sp = services.BuildServiceProvider();
        return sp.GetRequiredService<IOptions<ForwardedHeadersOptions>>().Value;
    }

    private static async Task<IHost> BuildHost()
    {
        var host = await new HostBuilder()
            .ConfigureWebHost(web => web
                .UseTestServer()
                .ConfigureServices(services =>
                {
                    services.AddEntraAuthForwardedHeaders();
                    services.PostConfigure<ForwardedHeadersOptions>(o =>
                    {
                        o.KnownIPNetworks.Clear();
                        o.KnownProxies.Clear();
                    });
                })
                .Configure(app =>
                {
                    app.UseForwardedHeaders();
                    app.Run(async ctx =>
                    {
                        var ip = ctx.Connection.RemoteIpAddress?.ToString() ?? "unknown";
                        await ctx.Response.WriteAsync(
                            $"ip={ip} scheme={ctx.Request.Scheme} host={ctx.Request.Host}");
                    });
                }))
            .StartAsync(TestContext.Current.CancellationToken);
        return host;
    }
}
