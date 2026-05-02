namespace Ftgo.Restaurants.Api;

/// <summary>Named authorization policy identifiers for Restaurants.Api. See DOCTRINE.md § "Authorization-policy doctrine".</summary>
public static class RestaurantsAuthorizationPolicies
{
    /// <summary>App-only (S2S, multi-tenant) — requires <c>roles=Restaurants.Read.All</c>, allow-listed <c>azp</c>, and rejects tokens carrying <c>scp</c>.</summary>
    public const string App = "RestaurantsApp";
}
