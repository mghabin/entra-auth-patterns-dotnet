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
    .ValidateOnStart();

builder.Services.Configure<DownstreamApiOptions>(
    builder.Configuration.GetSection(DownstreamApiOptions.DefaultSectionName));

builder.Services.AddEntraAuthTelemetry("Ftgo.AccountingService");
builder.Services.AddEntraAuthDownstreamApi();
builder.Services.AddSingleton<IAppTokenProvider, CertificateTokenProvider>();
builder.Services.AddHostedService<DownstreamProbeService>();

await builder.Build().RunAsync();
