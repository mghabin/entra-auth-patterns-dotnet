using Ftgo.Auth;
using Scalar.AspNetCore;

var builder = WebApplication.CreateBuilder(args);
builder.Services.AddEntraAuth(builder.Configuration);
builder.Services.AddEntraAuthWebTelemetry("Ftgo.OrderService");
builder.Services.AddEntraAuthProblemDetails();
builder.Services.AddOpenApi();
builder.Services.AddControllers();
builder.Services.AddHealthChecks();

var app = builder.Build();
app.UseEntraAuthProblemDetails();
app.UseAuthentication();
app.UseAuthorization();
app.MapControllers();
app.MapOpenApi();
app.MapScalarApiReference();
app.MapHealthChecks("/health");
await app.RunAsync();
