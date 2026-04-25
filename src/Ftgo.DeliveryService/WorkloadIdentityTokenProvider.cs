using Azure.Core;
using Azure.Identity;
using Ftgo.Auth;

namespace Ftgo.DeliveryService.Credentials;

/// <summary>App token via Workload Identity Federation. Reads the federated token from the platform (AKS workload identity, GitHub OIDC, …) via <c>AZURE_*</c> env vars; no secret stored.</summary>
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
