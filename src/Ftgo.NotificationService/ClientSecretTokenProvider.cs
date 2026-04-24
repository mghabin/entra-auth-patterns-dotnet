using System.ComponentModel.DataAnnotations;
using Ftgo.Auth;
using Microsoft.Identity.Client;

namespace Ftgo.NotificationService.Credentials;

internal sealed class AzureAdSecretOptions
{
    [Required] public string TenantId { get; init; } = string.Empty;
    [Required] public string ClientId { get; init; } = string.Empty;
    [Required] public string ClientSecret { get; set; } = string.Empty;
}

internal sealed class ClientSecretTokenProvider : IAppTokenProvider
{
    private readonly IConfidentialClientApplication _app;

    public ClientSecretTokenProvider(IOptions<AzureAdSecretOptions> aad)
    {
        _app = ConfidentialClientApplicationBuilder
            .Create(aad.Value.ClientId)
            .WithAuthority($"https://login.microsoftonline.com/{aad.Value.TenantId}")
            .WithClientSecret(aad.Value.ClientSecret)
            .Build();
    }

    public async ValueTask<string> GetAccessTokenAsync(string scope, CancellationToken cancellationToken)
    {
        var result = await _app.AcquireTokenForClient([scope]).ExecuteAsync(cancellationToken);
        return result.AccessToken;
    }
}
