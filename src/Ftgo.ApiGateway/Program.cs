using Ftgo.Auth;
using Microsoft.Identity.Web;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddEntraAuth(builder.Configuration);
builder.Services.AddEntraAuthTelemetry("Ftgo.ApiGateway");
builder.Services.AddEntraAuthProblemDetails();

builder.Services
    .AddMicrosoftIdentityWebApiAuthentication(builder.Configuration, "AzureAd", subscribeToJwtBearerMiddlewareDiagnosticsEvents: false)
    .EnableTokenAcquisitionToCallDownstreamApi()
        .AddDownstreamApi("Orders", builder.Configuration.GetSection("DownstreamApis:Orders"))
        .AddDownstreamApi("Restaurants", builder.Configuration.GetSection("DownstreamApis:Restaurants"))
        .AddDistributedTokenCaches();

builder.Services.AddDistributedMemoryCache();
builder.Services.AddControllers();

var app = builder.Build();
app.UseEntraAuthProblemDetails();
app.UseAuthentication();
app.UseAuthorization();
app.MapControllers();
await app.RunAsync();
