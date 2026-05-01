using Ftgo.Auth;
using Ftgo.Restaurants.Api;
using Scalar.AspNetCore;

var builder = WebApplication.CreateBuilder(args);
builder.Services.AddEntraAuth(builder.Configuration);
builder.Services.AddEntraAuthWebTelemetry("Ftgo.Restaurants.Api");
builder.Services.AddEntraAuthProblemDetails();
builder.Services.AddOpenApi();
builder.Services.AddControllers();
builder.Services.AddHealthChecks();

// Doctrine: app-only multi-tenant resource API. One named policy combining
// (a) Restaurants.Read.All app-role, (b) azp allow-list, (c) rejection of any token
// that also carries `scp`. Never use [Authorize(Roles=...)] when MapInboundClaims=false —
// the framework looks for ClaimTypes.Role, not the short `roles` name Entra emits.
// See eng-guide ch02 §10 / decision-trees.md Tree 4.
builder.Services.AddAuthorization(o =>
{
    o.AddAppPolicy(RestaurantsAuthorizationPolicies.App, "Restaurants.Read.All");
});

builder.Services.AddEntraAuthRateLimiter();
builder.Services.AddEntraAuthForwardedHeaders();

var app = builder.Build();
app.UseForwardedHeaders();
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
