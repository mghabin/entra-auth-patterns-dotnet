using System.ComponentModel.DataAnnotations;
using System.Security.Cryptography.X509Certificates;
using Azure.Identity;
using Azure.Security.KeyVault.Certificates;
using Ftgo.Auth;
using Microsoft.Identity.Client;

namespace Ftgo.AccountingService.Credentials;

internal sealed class AzureAdOptions
{
    [Required] public string TenantId { get; init; } = string.Empty;
    [Required] public string ClientId { get; init; } = string.Empty;
}

internal sealed class KeyVaultCertOptions
{
    [Required, Url] public string Uri { get; init; } = string.Empty;
    [Required] public string CertName { get; init; } = string.Empty;
}

/// <summary>
/// AccountingService — app token via CERTIFICATE (cert pulled from Key Vault with its private key).
/// </summary>
internal sealed class CertificateTokenProvider : IAppTokenProvider, IDisposable
{
    private readonly IConfidentialClientApplication _app;
    private readonly X509Certificate2 _certificate;

    public CertificateTokenProvider(
        IOptions<AzureAdOptions> aad,
        IOptions<KeyVaultCertOptions> kv)
    {
        var certClient = new CertificateClient(new Uri(kv.Value.Uri), new DefaultAzureCredential());
        _certificate = certClient.DownloadCertificate(kv.Value.CertName);

        _app = ConfidentialClientApplicationBuilder
            .Create(aad.Value.ClientId)
            .WithAuthority($"https://login.microsoftonline.com/{aad.Value.TenantId}")
            .WithCertificate(_certificate)
            .Build();
    }

    public async ValueTask<string> GetAccessTokenAsync(string scope, CancellationToken cancellationToken)
    {
        var result = await _app.AcquireTokenForClient([scope]).ExecuteAsync(cancellationToken);
        return result.AccessToken;
    }

    public void Dispose() => _certificate.Dispose();
}
