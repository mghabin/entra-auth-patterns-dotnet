using Ftgo.Auth;
using Microsoft.Identity.Web;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddEntraAuth(builder.Configuration, auth =>
{
    auth.EnableTokenAcquisitionToCallDownstreamApi(_ => { })
        .AddDownstreamApi("Orders", builder.Configuration.GetSection("DownstreamApis:Orders"))
        .AddDownstreamApi("Restaurants", builder.Configuration.GetSection("DownstreamApis:Restaurants"))
        .AddDistributedTokenCaches();
});
builder.Services.AddEntraAuthWebTelemetry("Ftgo.ApiGateway");
builder.Services.AddEntraAuthProblemDetails();
builder.Services.AddEntraAuthOpenApi(builder.Configuration, "orders.read");

builder.Services.AddDistributedMemoryCache();
builder.Services.AddControllers();

var app = builder.Build();
app.UseEntraAuthProblemDetails();
app.UseAuthentication();
app.UseAuthorization();
app.MapControllers();
app.MapEntraAuthScalar(builder.Configuration, "orders.read");
await app.RunAsync();
