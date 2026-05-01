using Ftgo.Auth;
using Ftgo.Orders.Api;
using Scalar.AspNetCore;

var builder = WebApplication.CreateBuilder(args);
builder.Services.AddEntraAuth(builder.Configuration);
builder.Services.AddEntraAuthWebTelemetry("Ftgo.Orders.Api");
builder.Services.AddEntraAuthProblemDetails();
builder.Services.AddOpenApi();
builder.Services.AddControllers();
builder.Services.AddHealthChecks();

// Doctrine: split delegated vs app into two named, mutually-exclusive policies.
// Never accept tokens that carry both `scp` and `roles` — that means a misconfigured app
// registration minted a mixed token. See eng-guide ch02 §10 / decision-trees.md Tree 4.
builder.Services.AddAuthorization(o =>
{
    o.AddDelegatedPolicy(OrdersAuthorizationPolicies.Delegated, "orders.read");
    o.AddAppPolicy(OrdersAuthorizationPolicies.App, "Orders.Process");
});

builder.Services.AddEntraAuthRateLimiter();
builder.Services.AddEntraAuthForwardedHeaders();

var app = builder.Build();
app.UseForwardedHeaders();
app.UseEntraAuthSecurityHeaders();
app.UseEntraAuthProblemDetails();
app.UseAuthentication();
app.UseAuthorization();
app.UseRateLimiter();
app.MapControllers().RequireRateLimiting(EntraAuthRateLimiterExtensions.DefaultPolicyName);
app.MapOpenApi().AllowAnonymous();
app.MapScalarApiReference().AllowAnonymous();
app.MapEntraAuthHealthChecks();
await app.RunAsync();
