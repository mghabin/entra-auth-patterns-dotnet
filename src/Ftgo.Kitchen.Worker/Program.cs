using Ftgo.Auth;
using Ftgo.Kitchen.Worker.Credentials;

var builder = Host.CreateApplicationBuilder(args);

builder.Services.Configure<DownstreamApiOptions>(
    builder.Configuration.GetSection(DownstreamApiOptions.DefaultSectionName));

builder.Services.Configure<ManagedIdentityOptions>(
    builder.Configuration.GetSection("ManagedIdentity"));

builder.Services.AddEntraAuthTelemetry("Ftgo.Kitchen.Worker");
builder.Services.AddEntraAuthDownstreamApi();
builder.Services.AddSingleton<IAppTokenProvider, ManagedIdentityTokenProvider>();
builder.Services.AddHostedService<DownstreamProbeService>();

await builder.Build().RunAsync();
