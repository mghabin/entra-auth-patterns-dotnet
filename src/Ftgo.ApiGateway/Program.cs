using Ftgo.ApiGateway;
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
builder.Services.AddEntraAuthWebTelemetry("Ftgo.ApiGateway", BffActivitySource.Name);
builder.Services.AddEntraAuthProblemDetails();
builder.Services.AddEntraAuthOpenApi(builder.Configuration, "orders.read");
builder.Services.AddHealthChecks();

builder.Services.AddDistributedMemoryCache();
builder.Services.AddControllers();
builder.Services.AddEntraAuthRateLimiter();
builder.Services.AddEntraAuthForwardedHeaders();

var app = builder.Build();
app.UseForwardedHeaders();
// API gateway serves the Scalar HTML UI; use the Scalar-friendly CSP preset so inline scripts/styles
// + the in-browser Auth Code + PKCE call to login.microsoftonline.com are not blocked.
app.UseEntraAuthSecurityHeaders(EntraAuthSecurityHeaderOptions.ScalarFriendly());
app.UseEntraAuthProblemDetails();
app.UseAuthentication();
app.UseAuthorization();
app.UseRateLimiter();
app.MapControllers().RequireRateLimiting(EntraAuthRateLimiterExtensions.DefaultPolicyName);
app.MapEntraAuthScalar(builder.Configuration, "orders.read");
app.MapEntraAuthHealthChecks();
await app.RunAsync();
