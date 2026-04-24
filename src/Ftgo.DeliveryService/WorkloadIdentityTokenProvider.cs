using Azure.Core;
using Azure.Identity;
using Ftgo.Auth;

namespace Ftgo.DeliveryService.Credentials;

/// <summary>
/// DeliveryService — app token via WORKLOAD IDENTITY FEDERATION (FIC). The
/// <see cref="WorkloadIdentityCredential"/> reads the federated token via the
/// AZURE_* env vars injected by the platform (e.g. Azure Workload Identity on AKS,
/// GitHub Actions OIDC). No secret is stored.
/// </summary>
internal sealed class WorkloadIdentityTokenProvider : IAppTokenProvider
{
    private readonly TokenCredential _credential = new WorkloadIdentityCredential();

    public async ValueTask<string> GetAccessTokenAsync(string scope, CancellationToken cancellationToken)
    {
        var token = await _credential.GetTokenAsync(
            new TokenRequestContext([scope]), cancellationToken);
        return token.Token;
    }
}
