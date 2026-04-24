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

    /// <summary>
    /// Optional user-assigned managed identity client ID. Leave empty to use system-assigned MI.
    /// </summary>
    public string? ManagedIdentityClientId { get; init; }
}

/// <summary>
/// AccountingService — app token via CERTIFICATE (cert pulled from Key Vault with its private key).
/// Cert is loaded asynchronously in <see cref="StartAsync"/> so the DI container stays free of sync I/O,
/// and the credential pulling the cert is restricted to <see cref="ManagedIdentityCredential"/>
/// (no fallback chain) for predictable production behaviour.
/// </summary>
internal sealed partial class CertificateTokenProvider : IAppTokenProvider, IHostedService, IDisposable
{
    private readonly IOptions<AzureAdOptions> _aad;
    private readonly IOptions<KeyVaultCertOptions> _kv;
    private readonly ILogger<CertificateTokenProvider> _logger;
    private IConfidentialClientApplication? _app;
    private X509Certificate2? _certificate;

    public CertificateTokenProvider(
        IOptions<AzureAdOptions> aad,
        IOptions<KeyVaultCertOptions> kv,
        ILogger<CertificateTokenProvider> logger)
    {
        _aad = aad;
        _kv = kv;
        _logger = logger;
    }

    public async Task StartAsync(CancellationToken cancellationToken)
    {
        var credential = string.IsNullOrWhiteSpace(_kv.Value.ManagedIdentityClientId)
            ? new ManagedIdentityCredential(ManagedIdentityId.SystemAssigned)
            : new ManagedIdentityCredential(
                ManagedIdentityId.FromUserAssignedClientId(_kv.Value.ManagedIdentityClientId));

        var certClient = new CertificateClient(new Uri(_kv.Value.Uri), credential);
        var downloaded = await certClient.DownloadCertificateAsync(
            new DownloadCertificateOptions(_kv.Value.CertName)
            {
                KeyStorageFlags = X509KeyStorageFlags.EphemeralKeySet,
            },
            cancellationToken).ConfigureAwait(false);

        _certificate = downloaded.Value;

        _app = ConfidentialClientApplicationBuilder
            .Create(_aad.Value.ClientId)
            .WithAuthority($"https://login.microsoftonline.com/{_aad.Value.TenantId}")
            .WithCertificate(_certificate)
            .Build();

        LogCertLoaded(_kv.Value.CertName);
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    public async ValueTask<string> GetAccessTokenAsync(string scope, CancellationToken cancellationToken)
    {
        if (_app is null)
        {
            throw new InvalidOperationException(
                $"{nameof(CertificateTokenProvider)} was used before {nameof(StartAsync)} completed.");
        }

        var result = await _app.AcquireTokenForClient([scope]).ExecuteAsync(cancellationToken).ConfigureAwait(false);
        return result.AccessToken;
    }

    public void Dispose() => _certificate?.Dispose();

    [LoggerMessage(EventId = 4000, Level = Microsoft.Extensions.Logging.LogLevel.Information, Message = "Certificate '{CertName}' loaded from Key Vault.")]
    private partial void LogCertLoaded(string certName);
}
