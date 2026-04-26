using Ftgo.Auth;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Hosting;
using Shouldly;
using Xunit;

namespace Ftgo.Auth.Tests;

public sealed class HealthChecksExtensionsTests
{
    [Fact]
    public async Task Live_endpoint_returns_200_when_only_dep_check_is_unhealthy()
    {
        using var host = await BuildHostAsync(includeUnhealthyReadyCheck: true);
        using var client = host.GetTestClient();

        var response = await client.GetAsync(new Uri("/health/live", UriKind.Relative), TestContext.Current.CancellationToken);

        response.StatusCode.ShouldBe(System.Net.HttpStatusCode.OK);
    }

    [Fact]
    public async Task Ready_endpoint_returns_503_when_a_ready_check_is_unhealthy()
    {
        using var host = await BuildHostAsync(includeUnhealthyReadyCheck: true);
        using var client = host.GetTestClient();

        var response = await client.GetAsync(new Uri("/health/ready", UriKind.Relative), TestContext.Current.CancellationToken);

        response.StatusCode.ShouldBe(System.Net.HttpStatusCode.ServiceUnavailable);
    }

    [Fact]
    public async Task Ready_endpoint_returns_200_when_no_ready_checks_are_registered()
    {
        using var host = await BuildHostAsync(includeUnhealthyReadyCheck: false);
        using var client = host.GetTestClient();

        var response = await client.GetAsync(new Uri("/health/ready", UriKind.Relative), TestContext.Current.CancellationToken);

        response.StatusCode.ShouldBe(System.Net.HttpStatusCode.OK);
    }

    private static async Task<IHost> BuildHostAsync(bool includeUnhealthyReadyCheck)
    {
        var builder = new HostBuilder().ConfigureWebHost(web =>
        {
            web.UseTestServer();
            web.ConfigureServices(services =>
            {
                services.AddRouting();
                var hc = services.AddHealthChecks();
                if (includeUnhealthyReadyCheck)
                {
                    hc.AddCheck("downstream-db", () => HealthCheckResult.Unhealthy(), tags: ["ready"]);
                }
            });
            web.Configure(app =>
            {
                app.UseRouting();
                app.UseEndpoints(e => e.MapEntraAuthHealthChecks());
            });
        });
        var host = await builder.StartAsync(TestContext.Current.CancellationToken);
        return host;
    }
}
