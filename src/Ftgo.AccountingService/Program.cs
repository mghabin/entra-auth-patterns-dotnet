using Ftgo.AccountingService.Credentials;
using Ftgo.Auth;

var builder = Host.CreateApplicationBuilder(args);

builder.Services
    .AddOptions<AzureAdOptions>()
    .Bind(builder.Configuration.GetSection("AzureAd"))
    .ValidateDataAnnotations()
    .ValidateOnStart();

builder.Services
    .AddOptions<KeyVaultCertOptions>()
    .Bind(builder.Configuration.GetSection("KeyVault"))
    .ValidateDataAnnotations()
    .Validate(
        kv => !string.IsNullOrWhiteSpace(kv.LocalPfxPath)
              || (!string.IsNullOrWhiteSpace(kv.Uri) && !string.IsNullOrWhiteSpace(kv.CertName)),
        "KeyVault must set either LocalPfxPath (local dev) OR both Uri and CertName (Key Vault).")
    .ValidateOnStart();

builder.Services.Configure<DownstreamApiOptions>(
    builder.Configuration.GetSection(DownstreamApiOptions.DefaultSectionName));

builder.Services.AddEntraAuthTelemetry("Ftgo.AccountingService");
builder.Services.AddEntraAuthDownstreamApi();

// Hosted services start in registration order; CertificateTokenProvider must run before
// DownstreamProbeService so the cert is loaded before the probe acquires a token.
builder.Services.AddSingleton<CertificateTokenProvider>();
builder.Services.AddSingleton<IAppTokenProvider>(sp => sp.GetRequiredService<CertificateTokenProvider>());
builder.Services.AddHostedService(sp => sp.GetRequiredService<CertificateTokenProvider>());
builder.Services.AddHostedService<DownstreamProbeService>();

await builder.Build().RunAsync();
