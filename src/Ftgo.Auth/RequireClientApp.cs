using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace Ftgo.Auth;

/// <summary>Authorization requirement enforcing an <c>azp</c>/<c>appid</c> allow-list and rejecting any token that carries <c>scp</c>.</summary>
public sealed class RequireClientAppRequirement : IAuthorizationRequirement
{
    public IReadOnlyCollection<string>? AllowedClientApps { get; }

    public RequireClientAppRequirement(params string[] allowedClientApps) =>
        AllowedClientApps = allowedClientApps is { Length: > 0 } ? allowedClientApps : null;
}

internal sealed class RequireClientAppHandler(IOptionsMonitor<EntraAuthOptions> options)
    : AuthorizationHandler<RequireClientAppRequirement>
{
    protected override Task HandleRequirementAsync(
        AuthorizationHandlerContext context,
        RequireClientAppRequirement requirement)
    {
        if (context.User.Identity?.IsAuthenticated != true)
        {
            return Task.CompletedTask;
        }

        // Defense in depth: a true app-only token never carries scp; reject if present.
        if (context.User.HasClaim(c =>
                string.Equals(c.Type, "scp", StringComparison.Ordinal) ||
                string.Equals(c.Type, "http://schemas.microsoft.com/identity/claims/scope", StringComparison.Ordinal)))
        {
            context.Fail(new AuthorizationFailureReason(this, "App-only endpoint received a user token."));
            return Task.CompletedTask;
        }

        var caller = context.User.FindFirst("azp")?.Value
                     ?? context.User.FindFirst("appid")?.Value;

        var allowList = requirement.AllowedClientApps ??
                        (IReadOnlyCollection<string>)options.CurrentValue.AllowedClientApps;

        if (string.IsNullOrEmpty(caller) || !allowList.Contains(caller, StringComparer.OrdinalIgnoreCase))
        {
            context.Fail(new AuthorizationFailureReason(this, $"Caller app '{caller}' is not allow-listed."));
            return Task.CompletedTask;
        }

        context.Succeed(requirement);
        return Task.CompletedTask;
    }
}

/// <summary>Sugar for the <c>EntraAuth:RequireClientApp</c> policy. Allow-list comes from <see cref="EntraAuthOptions.AllowedClientApps"/>; per-endpoint pinning is intentionally not supported.</summary>
[AttributeUsage(AttributeTargets.Class | AttributeTargets.Method, AllowMultiple = false, Inherited = true)]
public sealed class RequireClientAppAttribute : AuthorizeAttribute
{
    public RequireClientAppAttribute() : base(ClientAppPolicy.Name) { }
}

internal static class ClientAppPolicy
{
    internal const string Name = "EntraAuth:RequireClientApp";
}
