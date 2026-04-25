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
    [Url] public string? Uri { get; init; }

    public string? CertName { get; init; }

    public string? ManagedIdentityClientId { get; init; }

    /// <summary>Local-dev escape hatch: when set, the cert is loaded from disk and Key Vault is bypassed.</summary>
    public string? LocalPfxPath { get; init; }

    public string? LocalPfxPassword { get; init; }
}

// Cert is loaded in StartAsync (not the constructor) so DI stays free of sync I/O. The Key Vault
// credential is pinned to ManagedIdentityCredential — no DefaultAzureCredential fallback chain — for
// predictable production behaviour.
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
        _certificate = !string.IsNullOrWhiteSpace(_kv.Value.LocalPfxPath)
            ? LoadFromLocalPfx(_kv.Value)
            : await LoadFromKeyVaultAsync(_kv.Value, cancellationToken).ConfigureAwait(false);

        _app = ConfidentialClientApplicationBuilder
            .Create(_aad.Value.ClientId)
            .WithAuthority($"https://login.microsoftonline.com/{_aad.Value.TenantId}")
            .WithCertificate(_certificate)
            .Build();

        LogCertLoaded(_kv.Value.LocalPfxPath ?? _kv.Value.CertName ?? string.Empty);
    }

    private static X509Certificate2 LoadFromLocalPfx(KeyVaultCertOptions kv)
    {
        var path = kv.LocalPfxPath!;
        var bytes = File.ReadAllBytes(path);
        return X509CertificateLoader.LoadPkcs12(
            bytes,
            kv.LocalPfxPassword,
            X509KeyStorageFlags.EphemeralKeySet);
    }

    private static async Task<X509Certificate2> LoadFromKeyVaultAsync(KeyVaultCertOptions kv, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(kv.Uri) || string.IsNullOrWhiteSpace(kv.CertName))
        {
            throw new InvalidOperationException(
                "KeyVault:Uri and KeyVault:CertName are required when KeyVault:LocalPfxPath is not set.");
        }

        var credential = string.IsNullOrWhiteSpace(kv.ManagedIdentityClientId)
            ? new ManagedIdentityCredential(ManagedIdentityId.SystemAssigned)
            : new ManagedIdentityCredential(
                ManagedIdentityId.FromUserAssignedClientId(kv.ManagedIdentityClientId));

        var certClient = new CertificateClient(new Uri(kv.Uri), credential);
        var downloaded = await certClient.DownloadCertificateAsync(
            new DownloadCertificateOptions(kv.CertName)
            {
                KeyStorageFlags = X509KeyStorageFlags.EphemeralKeySet,
            },
            cancellationToken).ConfigureAwait(false);

        return downloaded.Value;
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
