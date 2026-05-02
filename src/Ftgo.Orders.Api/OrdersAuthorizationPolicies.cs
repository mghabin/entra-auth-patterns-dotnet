namespace Ftgo.Orders.Api;

/// <summary>Named authorization policy identifiers for Orders.Api. See DOCTRINE.md § "Authorization-policy doctrine".</summary>
public static class OrdersAuthorizationPolicies
{
    /// <summary>Delegated (user-token, OBO) — requires <c>scp=orders.read</c> and rejects tokens carrying <c>roles</c>.</summary>
    public const string Delegated = "OrdersDelegated";

    /// <summary>App-only (S2S) — requires <c>roles=Orders.Process</c>, allow-listed <c>azp</c>, and rejects tokens carrying <c>scp</c>.</summary>
    public const string App = "OrdersApp";
}
