using Microsoft.Extensions.DependencyInjection;
using OpenTelemetry.Metrics;
using OpenTelemetry.Trace;

namespace Ftgo.Auth;

/// <summary>
/// Web-host extension that layers AspNetCore tracing + metrics on top of
/// <see cref="TelemetryExtensions.AddEntraAuthTelemetry(IServiceCollection, string)"/>.
/// </summary>
public static class WebTelemetryExtensions
{
    public static IServiceCollection AddEntraAuthWebTelemetry(
        this IServiceCollection services,
        string serviceName)
    {
        ArgumentNullException.ThrowIfNull(services);

        services.AddEntraAuthTelemetry(serviceName);

        services
            .AddOpenTelemetry()
            .WithTracing(t => t.AddAspNetCoreInstrumentation())
            .WithMetrics(m => m.AddAspNetCoreInstrumentation());

        return services;
    }
}
