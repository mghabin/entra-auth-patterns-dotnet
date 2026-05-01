using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Diagnostics.HealthChecks;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Diagnostics.HealthChecks;

namespace Ftgo.Auth;

/// <summary>Split health probes following the ASP.NET Core / Kubernetes convention:
/// <list type="bullet">
///   <item><c>/health/live</c> — the process is alive (no external dependency checks). Used by liveness probes; failure means restart the pod.</item>
///   <item><c>/health/ready</c> — the process can serve traffic (all "ready" tag checks pass). Used by readiness probes; failure means stop sending traffic but don't restart.</item>
/// </list>
/// <para>Tag checks registered with <c>AddCheck&lt;T&gt;("name", tags: ["ready"])</c> participate in <c>/health/ready</c>.
/// Tag-less checks participate in liveness only.</para>
/// </summary>
public static class HealthChecksExtensions
{
    /// <summary>Maps <c>/health/live</c>, <c>/health/ready</c>, and <c>/health/startup</c>.
    /// Container Apps / K8s probes should hit these instead of a single combined <c>/health</c>:
    /// startup-probe → liveness-probe → readiness-probe (per Kubernetes
    /// <see href="https://kubernetes.io/docs/tasks/configure-pod-container/configure-liveness-readiness-startup-probes/">probe doc</see>).</summary>
    public static IEndpointRouteBuilder MapEntraAuthHealthChecks(this IEndpointRouteBuilder endpoints)
    {
        ArgumentNullException.ThrowIfNull(endpoints);

        endpoints.MapHealthChecks("/health/live", new HealthCheckOptions
        {
            Predicate = static _ => false,
            ResponseWriter = WriteJsonResponse,
        }).AllowAnonymous();

        endpoints.MapHealthChecks("/health/ready", new HealthCheckOptions
        {
            Predicate = static check => check.Tags.Contains("ready"),
            ResponseWriter = WriteJsonResponse,
        }).AllowAnonymous();

        // Startup checks are tagged "startup". Once startup checks pass, the platform stops
        // hitting this endpoint and switches to liveness + readiness for the rest of the pod's life.
        endpoints.MapHealthChecks("/health/startup", new HealthCheckOptions
        {
            Predicate = static check => check.Tags.Contains("startup"),
            ResponseWriter = WriteJsonResponse,
        }).AllowAnonymous();

        return endpoints;
    }

    private static Task WriteJsonResponse(HttpContext context, HealthReport report)
    {
        context.Response.ContentType = "application/json; charset=utf-8";
        var payload = System.Text.Json.JsonSerializer.Serialize(new
        {
            status = report.Status.ToString(),
            checks = report.Entries.Select(e => new
            {
                name = e.Key,
                status = e.Value.Status.ToString(),
                tags = e.Value.Tags,
            }),
        });
        return context.Response.WriteAsync(payload);
    }
}
