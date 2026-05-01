using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.Net.Http.Headers;

namespace Ftgo.Auth;

/// <summary>Default security response headers for an API surface (no HTML, no inline scripts, no embedding).
/// Doctrine: every response must declare its security posture explicitly — never rely on browser defaults.
/// Mirrors the OWASP Secure Headers Project recommendations for a JSON API
/// (<see href="https://owasp.org/www-project-secure-headers/"/>).</summary>
/// <remarks>
/// <para>Headers applied:</para>
/// <list type="bullet">
///   <item><c>Strict-Transport-Security</c> — 1 year, includeSubDomains, preload (HSTS, RFC 6797).</item>
///   <item><c>X-Content-Type-Options: nosniff</c> — disables MIME sniffing.</item>
///   <item><c>X-Frame-Options: DENY</c> — no embedding (also covered by CSP <c>frame-ancestors</c>).</item>
///   <item><c>Referrer-Policy: no-referrer</c> — APIs never need to leak Referer.</item>
///   <item><c>Permissions-Policy</c> — disables every browser feature; APIs don't use them.</item>
///   <item><c>Content-Security-Policy</c> — <c>default-src 'none'; frame-ancestors 'none'</c> (no script/style/image surfaces on a JSON API).</item>
///   <item><c>Cache-Control: no-store</c> — auth-bearing responses must not be cached.</item>
/// </list>
/// <para>Hosts that serve HTML (BFF + Scalar UI) should override CSP after calling this middleware
/// or pass <see cref="EntraAuthSecurityHeaderOptions.ContentSecurityPolicy"/> with a stricter HTML-friendly value.</para>
/// </remarks>
public static class EntraAuthSecurityHeadersExtensions
{
    /// <summary>Adds the default API security headers middleware. Call BEFORE <c>UseAuthentication</c>
    /// so even error responses on the auth path carry the headers.</summary>
    public static IApplicationBuilder UseEntraAuthSecurityHeaders(
        this IApplicationBuilder app,
        EntraAuthSecurityHeaderOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(app);
        var opts = options ?? new EntraAuthSecurityHeaderOptions();
        return app.Use(async (ctx, next) =>
        {
            var headers = ctx.Response.Headers;
            ctx.Response.OnStarting(() =>
            {
                headers[HeaderNames.StrictTransportSecurity] = opts.StrictTransportSecurity;
                headers[HeaderNames.XContentTypeOptions] = "nosniff";
                headers[HeaderNames.XFrameOptions] = "DENY";
                headers["Referrer-Policy"] = "no-referrer";
                headers["Permissions-Policy"] = opts.PermissionsPolicy;
                headers[HeaderNames.ContentSecurityPolicy] = opts.ContentSecurityPolicy;
                headers[HeaderNames.CacheControl] = "no-store";
                headers["X-Permitted-Cross-Domain-Policies"] = "none";
                return Task.CompletedTask;
            });
            await next();
        });
    }
}

/// <summary>Knobs for <see cref="EntraAuthSecurityHeadersExtensions.UseEntraAuthSecurityHeaders"/>.</summary>
public sealed class EntraAuthSecurityHeaderOptions
{
    /// <summary>HSTS header value. Default 1 year + includeSubDomains + preload (per RFC 6797 §6.1).</summary>
    public string StrictTransportSecurity { get; init; } = "max-age=31536000; includeSubDomains; preload";

    /// <summary>CSP value. Default <c>default-src 'none'; frame-ancestors 'none'</c> — appropriate for a JSON API.
    /// HTML hosts (BFF, Scalar UI) should call <see cref="ScalarFriendly"/>.</summary>
    public string ContentSecurityPolicy { get; init; } = "default-src 'none'; frame-ancestors 'none'";

    /// <summary>Permissions-Policy value. Default disables all browser-side capabilities.</summary>
    public string PermissionsPolicy { get; init; } =
        "accelerometer=(), camera=(), geolocation=(), gyroscope=(), magnetometer=(), microphone=(), payment=(), usb=()";

    /// <summary>CSP preset for hosts that serve the Scalar API-reference UI. Allows the inline scripts/styles
    /// Scalar emits, the Scalar CDN bundle, and the Microsoft Entra v2.0 OAuth/OIDC endpoints
    /// (<c>https://login.microsoftonline.com</c>) so the in-browser Auth Code + PKCE flow works.</summary>
    /// <remarks>
    /// Sources:
    /// <list type="bullet">
    ///   <item>Scalar.AspNetCore CSP guidance — <see href="https://github.com/scalar/scalar/blob/main/documentation/integrations/aspnetcore.md"/>.</item>
    ///   <item>Microsoft identity platform v2.0 endpoints — <see href="https://learn.microsoft.com/entra/identity-platform/v2-protocols"/>.</item>
    ///   <item>OWASP CSP cheatsheet — <see href="https://cheatsheetseries.owasp.org/cheatsheets/Content_Security_Policy_Cheat_Sheet.html"/>.</item>
    /// </list>
    /// </remarks>
    public static EntraAuthSecurityHeaderOptions ScalarFriendly() => new()
    {
        ContentSecurityPolicy = string.Join(' ',
            "default-src 'self';",
            "script-src 'self' 'unsafe-inline' https://cdn.jsdelivr.net;",
            "style-src 'self' 'unsafe-inline' https://cdn.jsdelivr.net;",
            "img-src 'self' data: https://cdn.jsdelivr.net;",
            "font-src 'self' data: https://cdn.jsdelivr.net;",
            "connect-src 'self' https://login.microsoftonline.com https://cdn.jsdelivr.net;",
            "frame-ancestors 'none';",
            "base-uri 'self';",
            "form-action 'self' https://login.microsoftonline.com"),
    };
}
