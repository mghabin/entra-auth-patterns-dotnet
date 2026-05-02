using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;

namespace Ftgo.Auth;

/// <summary>Hardened Kestrel + HTTPS-redirect defaults.
/// Defense-in-depth alongside the platform's own ingress (e.g. ACA enforces HTTPS at the edge).</summary>
public static class EntraAuthHostingExtensions
{
    public const long DefaultMaxRequestBodyBytes = 10L * 1024L * 1024L;

    /// <summary>Pins Kestrel limits suitable for a JSON-only API workload (10 MB body cap, 30 s headers, 65 s keep-alive).</summary>
    public static IWebHostBuilder ConfigureEntraAuthKestrel(this IWebHostBuilder webHost)
    {
        ArgumentNullException.ThrowIfNull(webHost);
        return webHost.ConfigureKestrel(options =>
        {
            options.Limits.MaxRequestBodySize = DefaultMaxRequestBodyBytes;
            options.Limits.RequestHeadersTimeout = TimeSpan.FromSeconds(30);
            options.Limits.KeepAliveTimeout = TimeSpan.FromSeconds(65);
            options.AddServerHeader = false;
        });
    }

    /// <summary>Registers a permanent (308) HTTPS redirect for any non-HTTPS request that bypasses platform ingress.</summary>
    public static IServiceCollection AddEntraAuthHttpsRedirection(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);
        return services.AddHttpsRedirection(o => o.RedirectStatusCode = StatusCodes.Status308PermanentRedirect);
    }
}
