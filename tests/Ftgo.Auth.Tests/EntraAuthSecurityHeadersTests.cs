using System.Net;
using Ftgo.Auth;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Shouldly;
using Xunit;

namespace Ftgo.Auth.Tests;

public sealed class EntraAuthSecurityHeadersTests
{
    [Fact]
    public async Task Default_headers_present_on_every_response()
    {
        using var host = await BuildHostAsync(useDefaults: true);
        using var client = host.GetTestClient();

        var response = await client.GetAsync(new Uri("/", UriKind.Relative), TestContext.Current.CancellationToken);

        response.Headers.GetValues("Strict-Transport-Security").ShouldContain("max-age=31536000; includeSubDomains; preload");
        response.Headers.GetValues("X-Content-Type-Options").ShouldContain("nosniff");
        response.Headers.GetValues("X-Frame-Options").ShouldContain("DENY");
        response.Headers.GetValues("Referrer-Policy").ShouldContain("no-referrer");
        response.Headers.GetValues("Permissions-Policy").ShouldNotBeEmpty();
        response.Headers.GetValues("Content-Security-Policy").ShouldContain("default-src 'none'; frame-ancestors 'none'");
        response.Headers.GetValues("X-Permitted-Cross-Domain-Policies").ShouldContain("none");
    }

    [Fact]
    public async Task Custom_csp_overrides_default()
    {
        var customCsp = "default-src 'self'; frame-ancestors 'none'";
        using var host = await BuildHostAsync(useDefaults: false, customCsp);
        using var client = host.GetTestClient();

        var response = await client.GetAsync(new Uri("/", UriKind.Relative), TestContext.Current.CancellationToken);

        response.Headers.GetValues("Content-Security-Policy").ShouldContain(customCsp);
    }

    [Fact]
    public async Task Cache_control_no_store_on_every_response()
    {
        using var host = await BuildHostAsync(useDefaults: true);
        using var client = host.GetTestClient();

        var response = await client.GetAsync(new Uri("/", UriKind.Relative), TestContext.Current.CancellationToken);

        response.Headers.CacheControl?.NoStore.ShouldBeTrue();
    }

    private static async Task<IHost> BuildHostAsync(bool useDefaults, string? customCsp = null)
    {
        var builder = new HostBuilder().ConfigureWebHost(web =>
        {
            web.UseTestServer();
            web.ConfigureServices(_ => { });
            web.Configure(app =>
            {
                if (useDefaults)
                {
                    app.UseEntraAuthSecurityHeaders();
                }
                else
                {
                    app.UseEntraAuthSecurityHeaders(new EntraAuthSecurityHeaderOptions
                    {
                        ContentSecurityPolicy = customCsp!,
                    });
                }
                app.Run(async ctx => await ctx.Response.WriteAsync("ok"));
            });
        });
        var host = await builder.StartAsync(TestContext.Current.CancellationToken);
        return host;
    }
}
