using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.Extensions.DependencyInjection;

namespace Ftgo.Auth;

/// <summary>Adds proxy-aware request handling so <see cref="Microsoft.AspNetCore.Http.HttpRequest.Scheme"/>,
/// <see cref="Microsoft.AspNetCore.Http.ConnectionInfo.RemoteIpAddress"/>, and <c>Host</c> reflect the
/// caller's view, not the proxy's. Required behind Azure Container Apps / Front Door / App Gateway / NGINX,
/// per the ASP.NET Core
/// <see href="https://learn.microsoft.com/aspnet/core/host-and-deploy/proxy-load-balancer">forwarded-headers
/// guidance</see> and RFC 7239.</summary>
public static class EntraAuthForwardedHeadersExtensions
{
    /// <summary>Configures <see cref="ForwardedHeadersOptions"/> to honour <c>X-Forwarded-For</c>,
    /// <c>X-Forwarded-Proto</c>, and <c>X-Forwarded-Host</c>. Default trusted-network list is empty —
    /// callers should add the proxy's IP/CIDR via <see cref="ForwardedHeadersOptions.KnownProxies"/>
    /// or <see cref="ForwardedHeadersOptions.KnownNetworks"/> in production. Container Apps' built-in
    /// envoy proxy is implicitly trusted via the loopback default.</summary>
    public static IServiceCollection AddEntraAuthForwardedHeaders(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);
        services.Configure<ForwardedHeadersOptions>(o =>
        {
            o.ForwardedHeaders = ForwardedHeaders.XForwardedFor
                                 | ForwardedHeaders.XForwardedProto
                                 | ForwardedHeaders.XForwardedHost;
            // Tighten in prod via KnownProxies/KnownNetworks; loopback (the platform sidecar) is implicit.
            o.ForwardLimit = 2;
        });
        return services;
    }
}
