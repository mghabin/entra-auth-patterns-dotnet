namespace Ftgo.Orders.Api;

/// <summary>
/// Named authorization policy identifiers for Orders.Api.
/// </summary>
/// <remarks>
/// Doctrine: APIs that accept both delegated <i>and</i> app callers expose two separate named
/// policies — never an OR-claims policy and never a single policy that ORs <c>scp</c> with
/// <c>roles</c>. Each policy is mutually exclusive with the other (a token with both <c>scp</c>
/// and <c>roles</c> is rejected by both). See dotnet-engineering-guide ch02 §10 and
/// <see href="https://github.com/mghabin/entra-auth-patterns-dotnet/blob/main/docs/decision-trees.md">decision-trees.md</see>
/// Tree 4.
/// </remarks>
public static class OrdersAuthorizationPolicies
{
    /// <summary>Delegated (user-token, OBO) callers — requires <c>scp=orders.read</c> and rejects tokens with <c>roles</c>.</summary>
    public const string Delegated = "OrdersDelegated";

    /// <summary>App-only (S2S) callers — requires <c>roles=Orders.Process</c>, an allow-listed <c>azp</c>, and rejects tokens with <c>scp</c>.</summary>
    public const string App = "OrdersApp";
}
