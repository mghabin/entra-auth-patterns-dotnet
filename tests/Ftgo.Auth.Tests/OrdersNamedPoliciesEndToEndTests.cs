using System.Net;
using System.Net.Http.Headers;
using System.Security.Claims;
using Ftgo.Auth;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using Shouldly;
using Xunit;

namespace Ftgo.Auth.Tests;

/// <summary>
/// End-to-end coverage of the two-named-policy doctrine wired via the public
/// <see cref="MixedClaimsAuthorizationExtensions.AddDelegatedPolicy"/> and
/// <see cref="MixedClaimsAuthorizationExtensions.AddAppPolicy"/> helpers — exactly the way
/// Ftgo.Orders.Api wires "OrdersDelegated"/"OrdersApp".
/// </summary>
public sealed class OrdersNamedPoliciesEndToEndTests : IAsyncLifetime
{
#pragma warning disable IDE1006 // const is conceptually a constant, not a private field
    private const string AllowedAppId = "11111111-1111-1111-1111-111111111111";
    private const string DelegatedPolicy = "OrdersDelegated";
    private const string AppPolicy = "OrdersApp";
#pragma warning restore IDE1006

    private IHost _host = null!;
    private HttpClient _client = null!;

    public async ValueTask InitializeAsync()
    {
        _host = await new HostBuilder()
            .ConfigureWebHost(web => web
                .UseTestServer()
                .ConfigureServices(services =>
                {
                    services.AddRouting();
                    services.AddSingleton<IOptionsMonitor<EntraAuthOptions>>(_ =>
                        new StaticOptionsMonitor<EntraAuthOptions>(new EntraAuthOptions
                        {
                            AllowedClientApps = [AllowedAppId],
                        }));

                    services.AddAuthentication(TestAuthHandler.SchemeName)
                        .AddScheme<TestAuthSchemeOptions, TestAuthHandler>(TestAuthHandler.SchemeName, _ => { });

                    services.AddAuthorization(o =>
                    {
                        o.AddDelegatedPolicy(DelegatedPolicy, "orders.read");
                        o.AddAppPolicy(AppPolicy, "Orders.Process");

                        // Pin the test scheme onto the doctrine policies so MapGet([Authorize(Policy=...)])
                        // resolves against the Test handler (mirrors the JWT scheme in production).
                        foreach (var name in new[] { DelegatedPolicy, AppPolicy })
                        {
                            var existing = o.GetPolicy(name)!;
                            o.AddPolicy(name, new AuthorizationPolicyBuilder(TestAuthHandler.SchemeName)
                                .Combine(existing)
                                .Build());
                        }
                    });
                    services.AddSingleton<IAuthorizationHandler, RequireClientAppHandler>();
                    services.AddSingleton<IAuthorizationHandler, RequireDelegatedScopeHandler>();
                    services.AddSingleton<IAuthorizationHandler, RequireAppRoleHandler>();
                })
                .Configure(app =>
                {
                    app.UseRouting();
                    app.UseAuthentication();
                    app.UseAuthorization();
                    app.UseEndpoints(e =>
                    {
                        e.MapGet("/whoami", () => Results.Ok("user")).RequireAuthorization(DelegatedPolicy);
                        e.MapGet("/system", () => Results.Ok("app")).RequireAuthorization(AppPolicy);
                    });
                }))
            .StartAsync();

        _client = _host.GetTestClient();
    }

    public async ValueTask DisposeAsync()
    {
        _client.Dispose();
        await _host.StopAsync();
        _host.Dispose();
    }

    private static HttpRequestMessage Request(string path, params (string type, string value)[] claims)
    {
        var req = new HttpRequestMessage(HttpMethod.Get, path);
        var encoded = string.Join("|", claims.Select(c => $"{c.type}={c.value}"));
        req.Headers.Authorization = new AuthenticationHeaderValue(TestAuthHandler.SchemeName, encoded);
        return req;
    }

    [Fact]
    public async Task OrdersDelegated_Allows_UserTokenWithScpOnly()
    {
        var resp = await _client.SendAsync(Request("/whoami", ("scp", "orders.read"), ("oid", "user-1")), TestContext.Current.CancellationToken);
        resp.StatusCode.ShouldBe(HttpStatusCode.OK);
    }

    [Fact]
    public async Task OrdersDelegated_RejectsTokenWithRoles()
    {
        // Mixed token: scp + roles → must be rejected by the delegated policy.
        var resp = await _client.SendAsync(
            Request("/whoami", ("scp", "orders.read"), ("roles", "Orders.Process")),
            TestContext.Current.CancellationToken);
        resp.StatusCode.ShouldBe(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task OrdersDelegated_Rejects_AppToken()
    {
        var resp = await _client.SendAsync(Request("/whoami", ("roles", "Orders.Process"), ("azp", AllowedAppId)), TestContext.Current.CancellationToken);
        resp.StatusCode.ShouldBe(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task OrdersApp_Allows_AppTokenWithRolesAndAllowListedAzp()
    {
        var resp = await _client.SendAsync(Request("/system", ("roles", "Orders.Process"), ("azp", AllowedAppId)), TestContext.Current.CancellationToken);
        resp.StatusCode.ShouldBe(HttpStatusCode.OK);
    }

    [Fact]
    public async Task OrdersApp_RejectsTokenWithScp()
    {
        // Mixed token: scp + roles → must be rejected by the app policy.
        var resp = await _client.SendAsync(
            Request("/system", ("roles", "Orders.Process"), ("scp", "orders.read"), ("azp", AllowedAppId)),
            TestContext.Current.CancellationToken);
        resp.StatusCode.ShouldBe(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task OrdersApp_Rejects_AppToken_WhenAzpNotAllowListed()
    {
        var resp = await _client.SendAsync(Request("/system", ("roles", "Orders.Process"), ("azp", Guid.NewGuid().ToString())), TestContext.Current.CancellationToken);
        resp.StatusCode.ShouldBe(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task OrdersApp_Rejects_UserToken()
    {
        var resp = await _client.SendAsync(Request("/system", ("scp", "orders.read")), TestContext.Current.CancellationToken);
        resp.StatusCode.ShouldBe(HttpStatusCode.Forbidden);
    }
}
