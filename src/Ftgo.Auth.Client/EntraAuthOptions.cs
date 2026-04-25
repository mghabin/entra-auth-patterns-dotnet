namespace Ftgo.Auth;

public sealed class EntraAuthOptions
{
    public const string SectionName = "EntraAuth";

    public TenancyMode Tenancy { get; init; } = TenancyMode.SingleTenant;

    /// <summary>Tenant allow-list. Required (non-empty) when <see cref="TenancyMode.MultiTenant"/>.</summary>
    public IReadOnlyList<string> AllowedTenantIds { get; init; } = [];

    /// <summary>Caller-app allow-list (<c>azp</c>/<c>appid</c>) for <c>[RequireClientApp]</c> endpoints.</summary>
    public IReadOnlyList<string> AllowedClientApps { get; init; } = [];

    /// <summary>Extra audiences accepted alongside <c>AzureAd:ClientId</c> (e.g. App ID URI during v1→v2 migration).</summary>
    public IReadOnlyList<string> AdditionalAudiences { get; init; } = [];
}

public enum TenancyMode
{
    SingleTenant,
    MultiTenant,
}
