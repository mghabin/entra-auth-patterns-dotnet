using Ftgo.Auth;
using Scalar.AspNetCore;

var builder = WebApplication.CreateBuilder(args);
builder.Services.AddEntraAuth(builder.Configuration);
builder.Services.AddEntraAuthWebTelemetry("Ftgo.Restaurants.Api");
builder.Services.AddEntraAuthProblemDetails();
builder.Services.AddOpenApi();
builder.Services.AddControllers();
builder.Services.AddHealthChecks();

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
