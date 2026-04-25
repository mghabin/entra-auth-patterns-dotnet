using Microsoft.Extensions.DependencyInjection;
using OpenTelemetry.Metrics;
using OpenTelemetry.Resources;
using OpenTelemetry.Trace;

namespace Ftgo.Auth;

/// <summary>OpenTelemetry traces + metrics for worker processes. The OTLP exporter is enabled only when <c>OTEL_EXPORTER_OTLP_ENDPOINT</c> is set, so local runs without a collector don't fail.</summary>
public static class TelemetryExtensions
{
    public static IServiceCollection AddEntraAuthTelemetry(
        this IServiceCollection services,
        string serviceName)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentException.ThrowIfNullOrWhiteSpace(serviceName);

        var hasOtlp = !string.IsNullOrWhiteSpace(
            Environment.GetEnvironmentVariable("OTEL_EXPORTER_OTLP_ENDPOINT"));

        services
            .AddOpenTelemetry()
            .ConfigureResource(r => r.AddService(serviceName))
            .WithTracing(t =>
            {
                t.AddHttpClientInstrumentation();
                if (hasOtlp) t.AddOtlpExporter();
            })
            .WithMetrics(m =>
            {
                m.AddHttpClientInstrumentation()
                 .AddRuntimeInstrumentation();
                if (hasOtlp) m.AddOtlpExporter();
            });

        return services;
    }
}
