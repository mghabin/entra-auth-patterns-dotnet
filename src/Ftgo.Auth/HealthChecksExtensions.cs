using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Diagnostics.HealthChecks;
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
    /// <summary>Maps <c>/health/live</c> (always-200 once the process started) and <c>/health/ready</c>
    /// (200 only when all checks tagged <c>ready</c> pass). Container Apps / K8s probes should hit these
    /// instead of a single combined <c>/health</c>.</summary>
    public static IEndpointRouteBuilder MapEntraAuthHealthChecks(this IEndpointRouteBuilder endpoints)
    {
        ArgumentNullException.ThrowIfNull(endpoints);

        endpoints.MapHealthChecks("/health/live", new HealthCheckOptions
        {
            Predicate = static _ => false,
        }).AllowAnonymous();

        endpoints.MapHealthChecks("/health/ready", new HealthCheckOptions
        {
            Predicate = static check => check.Tags.Contains("ready"),
        }).AllowAnonymous();

        return endpoints;
    }
}
