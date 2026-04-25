using Azure.Monitor.OpenTelemetry.Exporter;
using Microsoft.Extensions.DependencyInjection;
using OpenTelemetry.Metrics;
using OpenTelemetry.Resources;
using OpenTelemetry.Trace;

namespace Ftgo.Auth;

/// <summary>OpenTelemetry traces + metrics for worker processes. Exporters are conditionally registered:
/// the OTLP exporter activates when <c>OTEL_EXPORTER_OTLP_ENDPOINT</c> is set, the Azure Monitor
/// exporter activates when <c>APPLICATIONINSIGHTS_CONNECTION_STRING</c> is set. Local runs without
/// either env var don't fail.</summary>
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
        var aiConnectionString = Environment.GetEnvironmentVariable("APPLICATIONINSIGHTS_CONNECTION_STRING");
        var hasAzureMonitor = !string.IsNullOrWhiteSpace(aiConnectionString);

        services
            .AddOpenTelemetry()
            .ConfigureResource(r => r.AddService(serviceName))
            .WithTracing(t =>
            {
                t.AddHttpClientInstrumentation();
                if (hasOtlp) t.AddOtlpExporter();
                if (hasAzureMonitor) t.AddAzureMonitorTraceExporter(o => o.ConnectionString = aiConnectionString);
            })
            .WithMetrics(m =>
            {
                m.AddHttpClientInstrumentation()
                 .AddRuntimeInstrumentation();
                if (hasOtlp) m.AddOtlpExporter();
                if (hasAzureMonitor) m.AddAzureMonitorMetricExporter(o => o.ConnectionString = aiConnectionString);
            });

        return services;
    }
}
