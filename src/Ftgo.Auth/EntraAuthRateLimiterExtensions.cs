using System.Globalization;
using System.Security.Claims;
using System.Threading.RateLimiting;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.Extensions.DependencyInjection;

namespace Ftgo.Auth;

/// <summary>Per-caller rate limiter using ASP.NET Core's built-in
/// <see href="https://learn.microsoft.com/aspnet/core/performance/rate-limit"><c>System.Threading.RateLimiting</c></see>.
/// Doctrine: every public API enforces a default ceiling so a single misbehaving caller (legit or compromised)
/// can't exhaust capacity. Per-tenant + per-user partitioning keeps tenants isolated; unauthenticated requests
/// are bucketed by remote IP.</summary>
/// <remarks>
/// <para>Default policy <see cref="DefaultPolicyName"/> is a sliding-window limiter:</para>
/// <list type="bullet">
///   <item>Delegated user token: partition by <c>u|tid|oid</c> (Entra tenant + object id), <see cref="EntraAuthRateLimiterOptions.PermitLimit"/> per <see cref="EntraAuthRateLimiterOptions.Window"/>.</item>
///   <item>App-only token (service principal — `idtyp=app` or `roles` claim with no `scp`): partition by <c>a|tid|appid</c>, <see cref="EntraAuthRateLimiterOptions.AppPermitLimit"/> per window. App-only callers (gateway → downstream API, worker → API) are shared by every end-user behind that service principal, so the ceiling must be much higher than per-user.</item>
///   <item>Unauthenticated: partition by <c>ip|&lt;remote-ip&gt;</c>, <see cref="EntraAuthRateLimiterOptions.PermitLimit"/> per window.</item>
///   <item>429 with <c>Retry-After</c> on rejection (RFC 6585 §4 + §3).</item>
/// </list>
/// <para>Tune via <see cref="EntraAuthRateLimiterOptions"/>.</para>
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

        var validation = new EntraAuthRateLimiterOptionsValidator().Validate(name: null, opts);
        if (validation.Failed)
        {
            throw new ArgumentException(
                $"{nameof(EntraAuthRateLimiterOptions)} is invalid: {string.Join("; ", validation.Failures ?? Array.Empty<string>())}",
                nameof(configure));
        }

        services.AddRateLimiter(rl =>
        {
            rl.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
            var fallbackRetryAfterSeconds = ((int)opts.Window.TotalSeconds).ToString(CultureInfo.InvariantCulture);
            rl.OnRejected = (ctx, _) =>
            {
                // RFC 6585 §3 mandates Retry-After on 429. Sliding/fixed-window limiters only
                // populate the lease metadata when there's a queue; fall back to the window so
                // the header is present unconditionally.
                var seconds = ctx.Lease.TryGetMetadata(MetadataName.RetryAfter, out var retryAfter)
                    ? ((int)retryAfter.TotalSeconds).ToString(CultureInfo.InvariantCulture)
                    : fallbackRetryAfterSeconds;
                ctx.HttpContext.Response.Headers.RetryAfter = seconds;
                return ValueTask.CompletedTask;
            };

            rl.AddPolicy(DefaultPolicyName, ctx => BuildPartition(ctx, opts));

            rl.GlobalLimiter = PartitionedRateLimiter.Create<HttpContext, string>(ctx =>
                BuildPartition(ctx, opts));
        });

        return services;
    }

    private static RateLimitPartition<string> BuildPartition(HttpContext ctx, EntraAuthRateLimiterOptions opts)
    {
        var (key, isApp) = PartitionKeyAndKind(ctx);
        var permits = isApp ? opts.AppPermitLimit : opts.PermitLimit;
        return RateLimitPartition.GetSlidingWindowLimiter(
            partitionKey: key,
            factory: _ => new SlidingWindowRateLimiterOptions
            {
                PermitLimit = permits,
                Window = opts.Window,
                SegmentsPerWindow = opts.SegmentsPerWindow,
                QueueLimit = 0,
                QueueProcessingOrder = QueueProcessingOrder.OldestFirst,
                AutoReplenishment = true,
            });
    }

    /// <summary>Test-visible partition key helper. Returns <c>("u|tid|oid", false)</c> for delegated tokens,
    /// <c>("a|tid|appid", true)</c> for app-only tokens, and <c>("ip|&lt;remote-ip&gt;", false)</c> for anonymous.</summary>
    internal static (string Key, bool IsApp) PartitionKeyAndKind(HttpContext ctx)
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

            // App-only token detection. v2.0 tokens carry idtyp=app for service principals; older flows use
            // the absence of `scp` together with a `roles` claim. Either signal => bucket by app, not user.
            // https://learn.microsoft.com/entra/identity-platform/access-token-claims-reference#payload-claims
            var idtyp = user.FindFirstValue("idtyp");
            var hasScp = user.FindFirst("scp") is not null
                         || user.FindFirst("http://schemas.microsoft.com/identity/claims/scope") is not null;
            var hasRoles = user.FindFirst("roles") is not null
                           || user.FindFirst(ClaimTypes.Role) is not null;
            var isApp = string.Equals(idtyp, "app", StringComparison.OrdinalIgnoreCase)
                        || (!hasScp && hasRoles);

            if (isApp)
            {
                var appid = user.FindFirstValue("azp")
                             ?? user.FindFirstValue("appid")
                             ?? user.FindFirstValue("http://schemas.microsoft.com/identity/claims/appid")
                             ?? "unknown-appid";
                return ($"a|{tid}|{appid}", true);
            }

            var oid = user.FindFirstValue("oid")
                       ?? user.FindFirstValue("http://schemas.microsoft.com/identity/claims/objectidentifier")
                       ?? user.FindFirstValue("sub")
                       ?? "unknown-oid";
            return ($"u|{tid}|{oid}", false);
        }
        var ip = ctx.Connection.RemoteIpAddress?.ToString() ?? "unknown-ip";
        return ($"ip|{ip}", false);
    }

    /// <summary>Back-compat shim for tests / callers that just want the partition string.</summary>
    internal static string PartitionKey(HttpContext ctx) => PartitionKeyAndKind(ctx).Key;
}

/// <summary>Knobs for <see cref="EntraAuthRateLimiterExtensions"/>.
/// Defaults: 100 req / 60 s / 6-segment sliding window for users; 1000 req / 60 s for app-only callers.</summary>
public sealed class EntraAuthRateLimiterOptions
{
    /// <summary>Permit ceiling for a delegated-user partition (or anonymous IP partition).</summary>
    public int PermitLimit { get; set; } = 100;

    /// <summary>Permit ceiling for an app-only (service-principal) partition. Defaults to 10× <see cref="PermitLimit"/>
    /// because one service principal often fronts many end-users (gateway → downstream, worker → API).</summary>
    public int AppPermitLimit { get; set; } = 1000;

    public TimeSpan Window { get; set; } = TimeSpan.FromSeconds(60);
    public int SegmentsPerWindow { get; set; } = 6;
}

internal sealed class EntraAuthRateLimiterOptionsValidator : Microsoft.Extensions.Options.IValidateOptions<EntraAuthRateLimiterOptions>
{
    public Microsoft.Extensions.Options.ValidateOptionsResult Validate(string? name, EntraAuthRateLimiterOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        var failures = new List<string>();
        if (options.PermitLimit <= 0)
            failures.Add($"{nameof(EntraAuthRateLimiterOptions.PermitLimit)} must be > 0 (got {options.PermitLimit}).");
        if (options.AppPermitLimit <= 0)
            failures.Add($"{nameof(EntraAuthRateLimiterOptions.AppPermitLimit)} must be > 0 (got {options.AppPermitLimit}).");
        if (options.Window <= TimeSpan.Zero)
            failures.Add($"{nameof(EntraAuthRateLimiterOptions.Window)} must be > 0 (got {options.Window}).");
        if (options.SegmentsPerWindow <= 0)
            failures.Add($"{nameof(EntraAuthRateLimiterOptions.SegmentsPerWindow)} must be > 0 (got {options.SegmentsPerWindow}).");
        return failures.Count == 0
            ? Microsoft.Extensions.Options.ValidateOptionsResult.Success
            : Microsoft.Extensions.Options.ValidateOptionsResult.Fail(failures);
    }
}
