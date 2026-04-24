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

    /// <summary>
    /// Optional user-assigned managed identity client ID. Leave empty to use system-assigned MI.
    /// </summary>
    public string? ManagedIdentityClientId { get; init; }

    /// <summary>
    /// Optional path to a local PFX file. When set, the certificate is loaded from disk and
    /// Key Vault is bypassed entirely — useful for local development without a Key Vault.
    /// In production this should be left null so the cert is pulled from Key Vault using
    /// managed identity.
    /// </summary>
    public string? LocalPfxPath { get; init; }

    /// <summary>
    /// Optional passphrase for <see cref="LocalPfxPath"/>. Empty string is treated as no passphrase.
    /// </summary>
    public string? LocalPfxPassword { get; init; }
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
