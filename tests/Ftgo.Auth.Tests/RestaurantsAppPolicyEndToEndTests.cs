using System.Net;
using System.Net.Http.Headers;
using Ftgo.Auth;
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
/// End-to-end coverage of the multi-tenant app-only policy used by Ftgo.Restaurants.Api.
/// Mirrors <see cref="OrdersNamedPoliciesEndToEndTests"/> but for the single-policy (app-only)
/// resource API shape — proves the policy enforces role + azp allow-list + scp-rejection.
/// </summary>
/// <remarks>
/// This test exists specifically to catch the regression where an author writes
/// <c>[Authorize(Roles = "Restaurants.Read.All")] [RequireClientApp]</c> instead of using the
/// named policy. With <c>MapInboundClaims = false</c> (doctrine), the framework looks for
/// claims of type <see cref="System.Security.Claims.ClaimTypes.Role"/>, but the JWT carries
/// the role under the short name <c>roles</c>, so the role check is silently skipped — the
/// endpoint then accepts any allow-listed app regardless of role. The two `_PolicyMustEnforceRole`
/// tests exercise that exact failure mode.
/// </remarks>
public sealed class RestaurantsAppPolicyEndToEndTests : IAsyncLifetime
{
#pragma warning disable IDE1006
    private const string AllowedAppId = "22222222-2222-2222-2222-222222222222";
    private const string AppPolicy = "RestaurantsApp";
    private const string Role = "Restaurants.Read.All";
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
                        o.AddAppPolicy(AppPolicy, Role);

                        // Pin the test scheme onto the doctrine policy so MapGet([Authorize(Policy=...)])
                        // resolves against the Test handler (mirrors the JWT scheme in production).
                        var existing = o.GetPolicy(AppPolicy)!;
                        o.AddPolicy(AppPolicy, new AuthorizationPolicyBuilder(TestAuthHandler.SchemeName)
                            .Combine(existing)
                            .Build());
                    });
                    services.AddSingleton<IAuthorizationHandler, RequireClientAppHandler>();
                    services.AddSingleton<IAuthorizationHandler, RequireAppRoleHandler>();
                })
                .Configure(app =>
                {
                    app.UseRouting();
                    app.UseAuthentication();
                    app.UseAuthorization();
                    app.UseEndpoints(e =>
                    {
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
    public async Task RestaurantsApp_Allows_AppTokenWithRoleAndAllowListedAzp()
    {
        var resp = await _client.SendAsync(
            Request("/system", ("roles", Role), ("azp", AllowedAppId), ("tid", "tenant-a")),
            TestContext.Current.CancellationToken);
        resp.StatusCode.ShouldBe(HttpStatusCode.OK);
    }

    [Fact]
    public async Task RestaurantsApp_Rejects_TokenWithScp_MixedClaims()
    {
        var resp = await _client.SendAsync(
            Request("/system", ("roles", Role), ("scp", "restaurants.read"), ("azp", AllowedAppId)),
            TestContext.Current.CancellationToken);
        resp.StatusCode.ShouldBe(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task RestaurantsApp_Rejects_AppToken_WhenAzpNotAllowListed()
    {
        var resp = await _client.SendAsync(
            Request("/system", ("roles", Role), ("azp", Guid.NewGuid().ToString())),
            TestContext.Current.CancellationToken);
        resp.StatusCode.ShouldBe(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task RestaurantsApp_Rejects_UserToken()
    {
        var resp = await _client.SendAsync(
            Request("/system", ("scp", "restaurants.read")),
            TestContext.Current.CancellationToken);
        resp.StatusCode.ShouldBe(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task RestaurantsApp_Rejects_AllowListedAppWithoutRequiredRole()
    {
        // Regression guard for the [Authorize(Roles=...)] anti-pattern: with the named policy in place,
        // an allow-listed app that is MISSING the required role MUST be rejected. If a future author
        // re-introduces [Authorize(Roles="...")] with MapInboundClaims=false, the silent role-check
        // failure would let this request through and this test would fail.
        var resp = await _client.SendAsync(
            Request("/system", ("roles", "SomeOther.Role"), ("azp", AllowedAppId)),
            TestContext.Current.CancellationToken);
        resp.StatusCode.ShouldBe(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task RestaurantsApp_Rejects_AllowListedAppWithNoRolesClaim()
    {
        // Same regression guard: an allow-listed app with no `roles` claim at all must be rejected.
        var resp = await _client.SendAsync(
            Request("/system", ("azp", AllowedAppId), ("tid", "tenant-a")),
            TestContext.Current.CancellationToken);
        resp.StatusCode.ShouldBe(HttpStatusCode.Forbidden);
    }
}
