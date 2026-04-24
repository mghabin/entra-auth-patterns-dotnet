namespace Ftgo.Auth;

/// <summary>
/// Options bound from the "EntraAuth" configuration section.
/// </summary>
public sealed class EntraAuthOptions
{
    public const string SectionName = "EntraAuth";

    public TenancyMode Tenancy { get; init; } = TenancyMode.SingleTenant;

    /// <summary>
    /// Required for multi-tenant APIs: the explicit list of tenant IDs we've provisioned.
    /// An empty list with <see cref="TenancyMode.MultiTenant"/> is invalid.
    /// </summary>
    public IReadOnlyList<string> AllowedTenantIds { get; init; } = [];

    /// <summary>
    /// App-only callers that may hit endpoints decorated with <c>[RequireClientApp]</c>
    /// (matched against the token's <c>azp</c> or <c>appid</c> claim).
    /// </summary>
    public IReadOnlyList<string> AllowedClientApps { get; init; } = [];

    /// <summary>
    /// Additional audiences to accept alongside <c>AzureAd:ClientId</c> (e.g. the App ID URI
    /// during a v1 → v2 token migration). Leave empty by default.
    /// </summary>
    public IReadOnlyList<string> AdditionalAudiences { get; init; } = [];
}

public enum TenancyMode
{
    SingleTenant,
    MultiTenant,
}
