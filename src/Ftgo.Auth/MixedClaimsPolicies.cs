using System.Globalization;
using Microsoft.AspNetCore.Authorization;

namespace Ftgo.Auth;

/// <summary>
/// Authorization requirement enforcing the doctrine that a delegated (user) endpoint must see a
/// token with at least one of the required <c>scp</c> values <i>and</i> no <c>roles</c> claim.
/// </summary>
/// <remarks>
/// Backs the named "delegated" half of the doctrine that every API split between user and app
/// callers expose two mutually-exclusive named policies — never an OR-claims policy. See the
/// dotnet-engineering-guide ch02 §10 ("Never accept tokens that have both <c>scp</c> and <c>roles</c>
/// claims; that means a misconfigured app reg minted a token with both kinds of permissions.") and
/// <see href="https://github.com/mghabin/entra-auth-patterns-dotnet/blob/main/docs/decision-trees.md">decision-trees.md</see>
/// Tree 4.
/// </remarks>
public sealed class RequireDelegatedScopeRequirement : IAuthorizationRequirement
{
    public IReadOnlyCollection<string> RequiredScopes { get; }

    public RequireDelegatedScopeRequirement(params string[] requiredScopes)
    {
        ArgumentNullException.ThrowIfNull(requiredScopes);
        if (requiredScopes.Length == 0)
        {
            throw new ArgumentException("At least one scope must be supplied.", nameof(requiredScopes));
        }
        RequiredScopes = requiredScopes;
    }
}

internal sealed class RequireDelegatedScopeHandler
    : AuthorizationHandler<RequireDelegatedScopeRequirement>
{
    protected override Task HandleRequirementAsync(
        AuthorizationHandlerContext context,
        RequireDelegatedScopeRequirement requirement)
    {
        if (context.User.Identity?.IsAuthenticated != true)
        {
            return Task.CompletedTask;
        }

        // Doctrine: a delegated (user) token never carries `roles`. Reject mixed tokens —
        // they indicate a misconfigured app registration (both scope and app-role consents).
        if (context.User.HasClaim(c => string.Equals(c.Type, "roles", StringComparison.Ordinal)))
        {
            context.Fail(new AuthorizationFailureReason(this,
                "Delegated endpoint received a token that also carries `roles` (mixed scp+roles)."));
            return Task.CompletedTask;
        }

        var scpClaim = context.User.FindFirst("scp")?.Value
                       ?? context.User.FindFirst("http://schemas.microsoft.com/identity/claims/scope")?.Value;
        if (string.IsNullOrEmpty(scpClaim))
        {
            context.Fail(new AuthorizationFailureReason(this, "Delegated endpoint requires an `scp` claim."));
            return Task.CompletedTask;
        }

        var present = scpClaim.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (!requirement.RequiredScopes.Any(req =>
                present.Contains(req, StringComparer.Ordinal)))
        {
            context.Fail(new AuthorizationFailureReason(this,
                string.Create(CultureInfo.InvariantCulture,
                    $"Delegated endpoint requires one of scp [{string.Join(", ", requirement.RequiredScopes)}].")));
            return Task.CompletedTask;
        }

        context.Succeed(requirement);
        return Task.CompletedTask;
    }
}

/// <summary>
/// Authorization requirement enforcing the doctrine that an app-only endpoint must see a token
/// with the required app-role and no <c>scp</c> claim. Pair with <see cref="RequireClientAppRequirement"/>
/// to also enforce the <c>azp</c> allow-list.
/// </summary>
/// <remarks>
/// Mirrors <see cref="RequireDelegatedScopeRequirement"/> as the "app" half of the doctrine — see
/// the dotnet-engineering-guide ch02 §10 and
/// <see href="https://github.com/mghabin/entra-auth-patterns-dotnet/blob/main/docs/decision-trees.md">decision-trees.md</see>
/// Tree 4.
/// </remarks>
public sealed class RequireAppRoleRequirement : IAuthorizationRequirement
{
    public string RequiredRole { get; }

    public RequireAppRoleRequirement(string requiredRole)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(requiredRole);
        RequiredRole = requiredRole;
    }
}

internal sealed class RequireAppRoleHandler
    : AuthorizationHandler<RequireAppRoleRequirement>
{
    protected override Task HandleRequirementAsync(
        AuthorizationHandlerContext context,
        RequireAppRoleRequirement requirement)
    {
        if (context.User.Identity?.IsAuthenticated != true)
        {
            return Task.CompletedTask;
        }

        // Doctrine: an app-only token never carries `scp`. Reject mixed tokens.
        if (context.User.HasClaim(c =>
                string.Equals(c.Type, "scp", StringComparison.Ordinal) ||
                string.Equals(c.Type, "http://schemas.microsoft.com/identity/claims/scope", StringComparison.Ordinal)))
        {
            context.Fail(new AuthorizationFailureReason(this,
                "App-only endpoint received a token that also carries `scp` (mixed scp+roles)."));
            return Task.CompletedTask;
        }

        var hasRole = context.User.FindAll("roles")
            .Any(c => string.Equals(c.Value, requirement.RequiredRole, StringComparison.Ordinal));
        if (!hasRole)
        {
            context.Fail(new AuthorizationFailureReason(this,
                string.Create(CultureInfo.InvariantCulture,
                    $"App-only endpoint requires app-role '{requirement.RequiredRole}'.")));
            return Task.CompletedTask;
        }

        context.Succeed(requirement);
        return Task.CompletedTask;
    }
}

/// <summary>Helpers that register the two-named-policy doctrine on an <see cref="AuthorizationOptions"/>.</summary>
public static class MixedClaimsAuthorizationExtensions
{
    /// <summary>
    /// Registers a named delegated (user-token) policy. The policy requires an authenticated user, asserts the
    /// token carries at least one of the required <paramref name="requiredScopes"/> values in <c>scp</c>, and
    /// rejects any token that <i>also</i> carries a <c>roles</c> claim.
    /// </summary>
    /// <remarks>
    /// Pair with <see cref="AddAppPolicy"/> on the same API to expose the second named policy required by the
    /// doctrine. See ch02 §10 and
    /// <see href="https://github.com/mghabin/entra-auth-patterns-dotnet/blob/main/docs/decision-trees.md">decision-trees.md</see>
    /// Tree 4.
    /// </remarks>
    public static AuthorizationOptions AddDelegatedPolicy(
        this AuthorizationOptions options,
        string policyName,
        params string[] requiredScopes)
    {
        ArgumentNullException.ThrowIfNull(options);
        ArgumentException.ThrowIfNullOrWhiteSpace(policyName);

        options.AddPolicy(policyName, p =>
        {
            p.RequireAuthenticatedUser();
            p.Requirements.Add(new RequireDelegatedScopeRequirement(requiredScopes));
        });
        return options;
    }

    /// <summary>
    /// Registers a named app-only policy. The policy requires an authenticated user, asserts the token carries
    /// the <paramref name="requiredRole"/> in <c>roles</c>, rejects any token that <i>also</i> carries
    /// <c>scp</c>, and enforces the <c>azp</c>/<c>appid</c> allow-list via <see cref="RequireClientAppRequirement"/>.
    /// </summary>
    /// <remarks>
    /// Pair with <see cref="AddDelegatedPolicy"/> on the same API to expose the first named policy required by the
    /// doctrine. See ch02 §10 and
    /// <see href="https://github.com/mghabin/entra-auth-patterns-dotnet/blob/main/docs/decision-trees.md">decision-trees.md</see>
    /// Tree 4.
    /// </remarks>
    public static AuthorizationOptions AddAppPolicy(
        this AuthorizationOptions options,
        string policyName,
        string requiredRole)
    {
        ArgumentNullException.ThrowIfNull(options);
        ArgumentException.ThrowIfNullOrWhiteSpace(policyName);

        options.AddPolicy(policyName, p =>
        {
            p.RequireAuthenticatedUser();
            p.Requirements.Add(new RequireAppRoleRequirement(requiredRole));
            p.Requirements.Add(new RequireClientAppRequirement());
        });
        return options;
    }
}
