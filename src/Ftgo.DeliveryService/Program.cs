using Ftgo.Auth;
using Ftgo.DeliveryService.Credentials;

var builder = Host.CreateApplicationBuilder(args);

builder.Services.Configure<DownstreamApiOptions>(
    builder.Configuration.GetSection(DownstreamApiOptions.DefaultSectionName));

builder.Services.AddEntraAuthTelemetry("Ftgo.DeliveryService");
builder.Services.AddEntraAuthDownstreamApi();
builder.Services.AddSingleton<IAppTokenProvider, WorkloadIdentityTokenProvider>();
builder.Services.AddHostedService<DownstreamProbeService>();

await builder.Build().RunAsync();
