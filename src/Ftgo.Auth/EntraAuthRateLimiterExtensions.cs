using System.Globalization;
using System.Threading.RateLimiting;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.Extensions.DependencyInjection;
using System.Security.Claims;

namespace Ftgo.Auth;

/// <summary>Per-caller rate limiter using ASP.NET Core's built-in
/// <see href="https://learn.microsoft.com/aspnet/core/performance/rate-limit"><c>System.Threading.RateLimiting</c></see>.
/// Doctrine: every public API enforces a default ceiling so a single misbehaving caller (legit or compromised)
/// can't exhaust capacity. Per-tenant + per-user partitioning keeps tenants isolated; unauthenticated requests
/// are bucketed by remote IP.</summary>
/// <remarks>
/// <para>Default policy <see cref="DefaultPolicyName"/> is a sliding-window limiter:</para>
/// <list type="bullet">
///   <item>Authenticated: partition by <c>tid|oid</c> (Entra tenant + object id).</item>
///   <item>Unauthenticated: partition by <c>ip|&lt;remote-ip&gt;</c>.</item>
///   <item>100 requests per 60 seconds per partition; 429 with <c>Retry-After</c> on rejection.</item>
/// </list>
/// <para>Tune via <see cref="EntraAuthRateLimiterOptions"/>. Cite RFC 6585 §4 for the 429 status semantics.</para>
/// </remarks>
public static class EntraAuthRateLimiterExtensions
{
    public const string DefaultPolicyName = "EntraAuthDefault";

    /// <summary>Registers the rate-limiter services and the <see cref="DefaultPolicyName"/> policy.</summary>
    public static IServiceCollection AddEntraAuthRateLimiter(
        this IServiceCollection services,
        Action<EntraAuthRateLimiterOptions>? configure = null)
    {
        ArgumentNullException.ThrowIfNull(services);
        var opts = new EntraAuthRateLimiterOptions();
        configure?.Invoke(opts);

        services.AddRateLimiter(rl =>
        {
            rl.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
            rl.OnRejected = static (ctx, _) =>
            {
                if (ctx.Lease.TryGetMetadata(MetadataName.RetryAfter, out var retryAfter))
                {
                    ctx.HttpContext.Response.Headers.RetryAfter =
                        ((int)retryAfter.TotalSeconds).ToString(CultureInfo.InvariantCulture);
                }
                return ValueTask.CompletedTask;
            };

            rl.AddPolicy(DefaultPolicyName, ctx => RateLimitPartition.GetSlidingWindowLimiter(
                partitionKey: PartitionKey(ctx),
                factory: _ => new SlidingWindowRateLimiterOptions
                {
                    PermitLimit = opts.PermitLimit,
                    Window = opts.Window,
                    SegmentsPerWindow = opts.SegmentsPerWindow,
                    QueueLimit = 0,
                    QueueProcessingOrder = QueueProcessingOrder.OldestFirst,
                    AutoReplenishment = true,
                }));

            rl.GlobalLimiter = PartitionedRateLimiter.Create<HttpContext, string>(ctx =>
                RateLimitPartition.GetSlidingWindowLimiter(
                    partitionKey: PartitionKey(ctx),
                    factory: _ => new SlidingWindowRateLimiterOptions
                    {
                        PermitLimit = opts.PermitLimit,
                        Window = opts.Window,
                        SegmentsPerWindow = opts.SegmentsPerWindow,
                        QueueLimit = 0,
                        AutoReplenishment = true,
                    }));
        });

        return services;
    }

    internal static string PartitionKey(HttpContext ctx)
    {
        var user = ctx.User;
        if (user.Identity?.IsAuthenticated == true)
        {
            // Per Microsoft Entra access-token-claims-reference, `tid` and `oid` are the stable per-tenant
            // and per-principal identifiers. With MapInboundClaims=false they appear under the short names.
            // https://learn.microsoft.com/entra/identity-platform/access-token-claims-reference
            var tid = user.FindFirstValue("tid")
                       ?? user.FindFirstValue("http://schemas.microsoft.com/identity/claims/tenantid")
                       ?? "unknown-tid";
            var oid = user.FindFirstValue("oid")
                       ?? user.FindFirstValue("http://schemas.microsoft.com/identity/claims/objectidentifier")
                       ?? user.FindFirstValue("sub")
                       ?? "unknown-oid";
            return $"u|{tid}|{oid}";
        }
        var ip = ctx.Connection.RemoteIpAddress?.ToString() ?? "unknown-ip";
        return $"ip|{ip}";
    }
}

/// <summary>Knobs for <see cref="EntraAuthRateLimiterExtensions"/>.
/// Defaults: 100 req / 60 s / 6-segment sliding window. Cite ASP.NET Core docs:
/// <see href="https://learn.microsoft.com/aspnet/core/performance/rate-limit"/>.</summary>
public sealed class EntraAuthRateLimiterOptions
{
    public int PermitLimit { get; set; } = 100;
    public TimeSpan Window { get; set; } = TimeSpan.FromSeconds(60);
    public int SegmentsPerWindow { get; set; } = 6;
}
