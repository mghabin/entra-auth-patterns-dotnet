namespace Ftgo.Restaurants.Api;

/// <summary>
/// Named authorization policy identifiers for Restaurants.Api.
/// </summary>
/// <remarks>
/// Doctrine: app-only multi-tenant resource APIs expose one named policy that combines
/// the app-role check with the <c>azp</c> allow-list and rejects any token that also
/// carries <c>scp</c>. Never use <c>[Authorize(Roles = "...")]</c> on a JWT scheme that
/// has <c>MapInboundClaims = false</c> — the framework looks for claims of type
/// <c>ClaimTypes.Role</c>, but Entra emits the role claim under the short name <c>roles</c>,
/// so the role check would silently fail and either lock everyone out or (worse) lock no
/// one out depending on what other Authorize attributes are also applied. See
/// dotnet-engineering-guide ch02 §10 and
/// <see href="https://github.com/mghabin/entra-auth-patterns-dotnet/blob/main/docs/decision-trees.md">decision-trees.md</see>
/// Tree 4.
/// </remarks>
public static class RestaurantsAuthorizationPolicies
{
    /// <summary>App-only (S2S, multi-tenant) callers — requires <c>roles=Restaurants.Read.All</c>,
    /// an allow-listed <c>azp</c>, and rejects tokens with <c>scp</c>.</summary>
    public const string App = "RestaurantsApp";
}
