using Ftgo.Auth;
using Ftgo.Orders.Api;
using Scalar.AspNetCore;

var builder = WebApplication.CreateBuilder(args);
builder.WebHost.ConfigureEntraAuthKestrel();
builder.Services.AddEntraAuthHttpsRedirection();
builder.Services.AddEntraAuth(builder.Configuration);
builder.Services.AddEntraAuthWebTelemetry("Ftgo.Orders.Api");
builder.Services.AddEntraAuthProblemDetails();
builder.Services.AddOpenApi();
builder.Services.AddControllers();
builder.Services.AddHealthChecks();

builder.Services.AddAuthorization(o =>
{
    o.AddDelegatedPolicy(OrdersAuthorizationPolicies.Delegated, "orders.read");
    o.AddAppPolicy(OrdersAuthorizationPolicies.App, "Orders.Process");
});

builder.Services.AddEntraAuthRateLimiter();
builder.Services.AddEntraAuthForwardedHeaders();

var app = builder.Build();
app.UseForwardedHeaders();
app.UseHttpsRedirection();
app.UseEntraAuthSecurityHeaders(EntraAuthSecurityHeaderOptions.ScalarFriendly());
app.UseEntraAuthProblemDetails();
app.UseAuthentication();
app.UseAuthorization();
app.UseRateLimiter();
app.MapControllers().RequireRateLimiting(EntraAuthRateLimiterExtensions.DefaultPolicyName);
app.MapOpenApi().AllowAnonymous();
app.MapScalarApiReference().AllowAnonymous();
app.MapEntraAuthHealthChecks();
await app.RunAsync();
