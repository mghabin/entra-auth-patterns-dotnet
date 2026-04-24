using Ftgo.Auth;
using Ftgo.NotificationService.Credentials;

// NotificationService — app token via CLIENT SECRET. ⚠ Anti-pattern in production; prefer MI > FIC > Cert.
// The secret must be injected at runtime (env var sourced from Key Vault / Secrets Manager).

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
