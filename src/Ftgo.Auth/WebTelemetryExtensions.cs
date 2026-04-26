using Microsoft.Extensions.DependencyInjection;
using OpenTelemetry.Metrics;
using OpenTelemetry.Trace;

namespace Ftgo.Auth;

public static class WebTelemetryExtensions
{
    public static IServiceCollection AddEntraAuthWebTelemetry(
        this IServiceCollection services,
        string serviceName,
        params string[] additionalActivitySources)
    {
        ArgumentNullException.ThrowIfNull(services);

        services.AddEntraAuthTelemetry(serviceName, additionalActivitySources);

        services
            .AddOpenTelemetry()
            .WithTracing(t => t.AddAspNetCoreInstrumentation())
            .WithMetrics(m => m.AddAspNetCoreInstrumentation());

        return services;
    }
}
