using Ftgo.Auth;
using Ftgo.NotificationService.Credentials;

// ⚠ Anti-pattern: client-secret credential is included only for contrast. Prefer MI > FIC > Cert.
// The secret is sourced at runtime from FTGO_NOTIFICATIONSERVICE_CLIENT_SECRET (Key Vault-injected).

var builder = Host.CreateApplicationBuilder(args);

builder.Services
    .AddOptions<AzureAdSecretOptions>()
    .Bind(builder.Configuration.GetSection("AzureAd"))
    .Configure(o => o.ClientSecret = Environment.GetEnvironmentVariable("FTGO_NOTIFICATIONSERVICE_CLIENT_SECRET") ?? string.Empty)
    .ValidateDataAnnotations()
    .ValidateOnStart();

builder.Services.Configure<DownstreamApiOptions>(
    builder.Configuration.GetSection(DownstreamApiOptions.DefaultSectionName));

builder.Services.AddEntraAuthTelemetry("Ftgo.NotificationService");
builder.Services.AddEntraAuthDownstreamApi();
builder.Services.AddSingleton<IAppTokenProvider, ClientSecretTokenProvider>();
builder.Services.AddHostedService<DownstreamProbeService>();

await builder.Build().RunAsync();
