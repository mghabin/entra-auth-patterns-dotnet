using Ftgo.Auth;

var builder = WebApplication.CreateBuilder(args);
builder.Services.AddEntraAuth(builder.Configuration);
builder.Services.AddEntraAuthTelemetry("Ftgo.RestaurantService");
builder.Services.AddEntraAuthProblemDetails();
builder.Services.AddControllers();

var app = builder.Build();
app.UseEntraAuthProblemDetails();
app.UseAuthentication();
app.UseAuthorization();
app.MapControllers();
await app.RunAsync();
