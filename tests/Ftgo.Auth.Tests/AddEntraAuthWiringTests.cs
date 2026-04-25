using System.Net;
using System.Net.Http.Headers;
using System.Security.Claims;
using System.Text.Encodings.Web;
using Ftgo.Auth;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Microsoft.Identity.Web;
using Shouldly;
using Xunit;

namespace Ftgo.Auth.Tests;

public sealed class AddEntraAuthWiringTests
{
    private static IConfiguration BuildConfig(string clientId, string? tenantId = null)
    {
        var dict = new Dictionary<string, string?>
        {
            ["AzureAd:Instance"] = "https://login.microsoftonline.com/",
            ["AzureAd:TenantId"] = tenantId ?? Guid.NewGuid().ToString(),
            ["AzureAd:ClientId"] = clientId,
        };
        return new ConfigurationBuilder().AddInMemoryCollection(dict).Build();
    }

    [Fact]
    public void AddEntraAuth_InvokesConfigureAuthenticationCallback_ExactlyOnce()
    {
        var services = new ServiceCollection();
        services.AddSingleton(NullLoggerFactory.Instance);
        services.AddDataProtection();
        var config = BuildConfig(Guid.NewGuid().ToString());

        var invocations = 0;
        MicrosoftIdentityWebApiAuthenticationBuilder? captured = null;
        services.AddEntraAuth(config, auth =>
        {
            invocations++;
            captured = auth;
        });

        invocations.ShouldBe(1);
        captured.ShouldNotBeNull();
    }

    [Fact]
    public void AddEntraAuth_PostConfiguresJwtBearerScheme_PinsAudience()
    {
        var clientId = Guid.NewGuid().ToString();
        var services = new ServiceCollection();
        services.AddSingleton(NullLoggerFactory.Instance);
        services.AddDataProtection();
        var config = BuildConfig(clientId);
        services.AddSingleton<IConfiguration>(config);
        services.AddEntraAuth(config);

        using var sp = services.BuildServiceProvider();
        var jwt = sp.GetRequiredService<IOptionsMonitor<JwtBearerOptions>>()
            .Get(JwtBearerDefaults.AuthenticationScheme);

        jwt.TokenValidationParameters.ValidAudiences.ShouldNotBeNull();
        jwt.TokenValidationParameters.ValidAudiences.ShouldContain(clientId);
    }

    [Fact]
    public void AddEntraAuth_PostConfiguresJwtBearerScheme_InstallsIssuerValidatorForMultiTenant()
    {
        var clientId = Guid.NewGuid().ToString();
        var allowedTenant = Guid.NewGuid().ToString();
        var services = new ServiceCollection();
        services.AddSingleton(NullLoggerFactory.Instance);
        services.AddDataProtection();

        var dict = new Dictionary<string, string?>
        {
            ["AzureAd:Instance"] = "https://login.microsoftonline.com/",
            ["AzureAd:TenantId"] = "organizations",
            ["AzureAd:ClientId"] = clientId,
            ["EntraAuth:Tenancy"] = "MultiTenant",
            ["EntraAuth:AllowedTenantIds:0"] = allowedTenant,
        };
        var config = new ConfigurationBuilder().AddInMemoryCollection(dict).Build();
        services.AddSingleton<IConfiguration>(config);
        services.AddEntraAuth(config);

        using var sp = services.BuildServiceProvider();
        var jwt = sp.GetRequiredService<IOptionsMonitor<JwtBearerOptions>>()
            .Get(JwtBearerDefaults.AuthenticationScheme);

        jwt.TokenValidationParameters.IssuerValidator.ShouldNotBeNull();
    }

    [Fact]
    public Task AddEntraAuth_ValidateOnStart_FailsHostStart_WhenMultiTenantHasEmptyAllowList()
    {
        var dict = new Dictionary<string, string?>
        {
            ["AzureAd:Instance"] = "https://login.microsoftonline.com/",
            ["AzureAd:TenantId"] = "organizations",
            ["AzureAd:ClientId"] = Guid.NewGuid().ToString(),
            ["EntraAuth:Tenancy"] = "MultiTenant",
        };

        var hostBuilder = Host.CreateDefaultBuilder()
            .ConfigureAppConfiguration(c => c.Sources.Clear())
            .ConfigureAppConfiguration(c => c.AddInMemoryCollection(dict))
            .ConfigureServices((ctx, services) =>
            {
                services.AddDataProtection();
                services.AddEntraAuth(ctx.Configuration);
            });

        return Should.ThrowAsync<OptionsValidationException>(async () =>
        {
            using var host = hostBuilder.Build();
            await host.StartAsync(TestContext.Current.CancellationToken);
        });
    }
}

public sealed class RequireClientAppEndToEndTests : IAsyncLifetime
{
#pragma warning disable IDE1006 // const is conceptually a constant, not a private field
    private const string AllowedAppId = "11111111-1111-1111-1111-111111111111";
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
                        o.AddPolicy(ClientAppPolicy.Name, p =>
                        {
                            p.AuthenticationSchemes = [TestAuthHandler.SchemeName];
                            p.RequireAuthenticatedUser();
                            p.Requirements.Add(new RequireClientAppRequirement());
                        });
                    });
                    services.AddSingleton<IAuthorizationHandler, RequireClientAppHandler>();
                })
                .Configure(app =>
                {
                    app.UseRouting();
                    app.UseAuthentication();
                    app.UseAuthorization();
                    app.UseEndpoints(e =>
                    {
                        e.MapGet("/protected", [RequireClientApp] () => Results.Ok("ok"));
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

    private static HttpRequestMessage Request(params (string type, string value)[] claims)
    {
        var req = new HttpRequestMessage(HttpMethod.Get, "/protected");
        var encoded = string.Join("|", claims.Select(c => $"{c.type}={c.value}"));
        req.Headers.Authorization = new AuthenticationHeaderValue(TestAuthHandler.SchemeName, encoded);
        return req;
    }

    [Fact]
    public async Task Returns403_WhenAzpClaimMissing()
    {
        var resp = await _client.SendAsync(Request(("sub", "user1")), TestContext.Current.CancellationToken);
        resp.StatusCode.ShouldBe(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task Returns403_WhenAzpNotAllowListed()
    {
        var resp = await _client.SendAsync(Request(("azp", Guid.NewGuid().ToString())), TestContext.Current.CancellationToken);
        resp.StatusCode.ShouldBe(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task Returns200_WhenAzpAllowListed_AndNoScpClaim()
    {
        var resp = await _client.SendAsync(Request(("azp", AllowedAppId)), TestContext.Current.CancellationToken);
        resp.StatusCode.ShouldBe(HttpStatusCode.OK);
    }

    [Fact]
    public async Task Returns403_WhenAzpAllowListed_ButHasScpClaim()
    {
        var resp = await _client.SendAsync(Request(("azp", AllowedAppId), ("scp", "user_impersonation")), TestContext.Current.CancellationToken);
        resp.StatusCode.ShouldBe(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task Returns401_WhenNoAuthorizationHeader()
    {
        var resp = await _client.GetAsync("/protected", TestContext.Current.CancellationToken);
        resp.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
    }
}

#pragma warning disable S2094 // marker options class for our test scheme
internal sealed class TestAuthSchemeOptions : AuthenticationSchemeOptions { }
#pragma warning restore S2094

internal sealed class TestAuthHandler : AuthenticationHandler<TestAuthSchemeOptions>
{
    public const string SchemeName = "Test";

    public TestAuthHandler(IOptionsMonitor<TestAuthSchemeOptions> opts, Microsoft.Extensions.Logging.ILoggerFactory logger, UrlEncoder encoder)
        : base(opts, logger, encoder) { }

    protected override Task<AuthenticateResult> HandleAuthenticateAsync()
    {
        if (!Request.Headers.TryGetValue("Authorization", out var h) ||
            string.IsNullOrEmpty(h.ToString()))
        {
            return Task.FromResult(AuthenticateResult.NoResult());
        }

        var raw = h.ToString();
        var prefix = SchemeName + " ";
        if (!raw.StartsWith(prefix, StringComparison.Ordinal))
        {
            return Task.FromResult(AuthenticateResult.NoResult());
        }

        var payload = raw[prefix.Length..];
        var claims = payload.Split('|', StringSplitOptions.RemoveEmptyEntries)
            .Select(p => p.Split('=', 2))
            .Where(parts => parts.Length == 2)
            .Select(parts => new Claim(parts[0], parts[1]))
            .ToList();

        var identity = new ClaimsIdentity(claims, SchemeName);
        var principal = new ClaimsPrincipal(identity);
        var ticket = new AuthenticationTicket(principal, SchemeName);
        return Task.FromResult(AuthenticateResult.Success(ticket));
    }
}

internal sealed class StaticOptionsMonitor<T>(T value) : IOptionsMonitor<T>
{
    public T CurrentValue { get; } = value;
    public T Get(string? name) => CurrentValue;
    public IDisposable? OnChange(Action<T, string?> listener) => null;
}
