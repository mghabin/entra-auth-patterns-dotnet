using Azure.Core;
using Azure.Identity;
using Ftgo.Auth;

namespace Ftgo.KitchenService.Credentials;

internal sealed class ManagedIdentityOptions
{
    public string? UserAssignedClientId { get; init; }
}

/// <summary>App token via Managed Identity (system- or user-assigned). Use inside Azure.</summary>
internal sealed class ManagedIdentityTokenProvider(IOptions<ManagedIdentityOptions> options)
    : IAppTokenProvider
{
    private readonly TokenCredential _credential =
        string.IsNullOrWhiteSpace(options.Value.UserAssignedClientId)
            ? new ManagedIdentityCredential(ManagedIdentityId.SystemAssigned)
            : new ManagedIdentityCredential(
                ManagedIdentityId.FromUserAssignedClientId(options.Value.UserAssignedClientId));

    public async ValueTask<string> GetAccessTokenAsync(string scope, CancellationToken cancellationToken)
    {
        var token = await _credential.GetTokenAsync(
            new TokenRequestContext([scope]), cancellationToken);
        return token.Token;
    }
}
