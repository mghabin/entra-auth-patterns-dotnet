using System.Diagnostics;
using System.Reflection;

namespace Ftgo.ApiGateway;

/// <summary>
/// Custom <see cref="ActivitySource"/> for BFF flows. Wraps each downstream call (OBO,
/// app-only / S2S, multi-tenant S2S) in a named span so the trace tree in App Insights
/// or Jaeger shows the auth flow explicitly (e.g. <c>bff.checkout.via_obo</c>) instead of
/// just the underlying outgoing HTTP request.
/// </summary>
/// <remarks>
/// Registered via <c>AddEntraAuthWebTelemetry("Ftgo.ApiGateway", BffActivitySource.Name)</c>.
/// Span tags use the OTel semantic conventions where possible:
/// <list type="bullet">
///   <item><c>ftgo.auth.flow</c> — <c>obo</c> | <c>app_only</c> | <c>app_only_multitenant</c></item>
///   <item><c>ftgo.downstream.api</c> — logical downstream name (Orders, Restaurants)</item>
/// </list>
/// </remarks>
public static class BffActivitySource
{
    public const string Name = "Ftgo.ApiGateway.Bff";

    public static readonly ActivitySource Instance = new(
        Name,
        typeof(BffActivitySource).Assembly.GetName().Version?.ToString() ?? "0.0.0");
}
