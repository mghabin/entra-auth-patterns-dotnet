using System.Diagnostics;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Identity.Abstractions;
using Microsoft.Identity.Web.Resource;

namespace Ftgo.ApiGateway.Controllers;

[ApiController]
[Route("api/[controller]")]
[Authorize]
public sealed class CheckoutController(IDownstreamApi downstream, IConfiguration config) : ControllerBase
{
    [HttpGet("via-obo")]
    [RequiredScope("orders.read")]
    public async Task<IActionResult> ViaObo()
    {
        using var activity = BffActivitySource.Instance.StartActivity("bff.checkout.via_obo", ActivityKind.Internal);
        activity?.SetTag("ftgo.auth.flow", "obo");
        activity?.SetTag("ftgo.downstream.api", "Orders");

        var resp = await downstream.CallApiForUserAsync("Orders", o => o.RelativePath = "api/orders/whoami");
        var body = await resp.Content.ReadAsStringAsync();
        return Content(body, "application/json");
    }

    [HttpGet("via-s2s")]
    [RequiredScope("orders.read")]
    public async Task<IActionResult> ViaS2S()
    {
        using var activity = BffActivitySource.Instance.StartActivity("bff.checkout.via_s2s", ActivityKind.Internal);
        activity?.SetTag("ftgo.auth.flow", "app_only");
        activity?.SetTag("ftgo.downstream.api", "Orders");

        var resp = await downstream.CallApiForAppAsync("Orders", o =>
        {
            o.RelativePath = "api/orders/system";
            o.Scopes = AppScopes("Orders");
        });
        var body = await resp.Content.ReadAsStringAsync();
        return Content(body, "application/json");
    }

    [HttpGet("via-s2s-multitenant")]
    [RequiredScope("orders.read")]
    public async Task<IActionResult> ViaS2SMultiTenant()
    {
        using var activity = BffActivitySource.Instance.StartActivity("bff.checkout.via_s2s_multitenant", ActivityKind.Internal);
        activity?.SetTag("ftgo.auth.flow", "app_only_multitenant");
        activity?.SetTag("ftgo.downstream.api", "Restaurants");

        var resp = await downstream.CallApiForAppAsync("Restaurants", o =>
        {
            o.RelativePath = "api/restaurants/system";
            o.Scopes = AppScopes("Restaurants");
        });
        var body = await resp.Content.ReadAsStringAsync();
        return Content(body, "application/json");
    }

    // App-only (client-credentials) calls require the `{AppIdUri}/.default` scope, which is kept in
    // a separate config key so OBO and s2s don't accidentally share it.
    private string[] AppScopes(string downstreamName) =>
        config.GetSection($"DownstreamApis:{downstreamName}:AppPermissionScopes").Get<string[]>()
            ?? throw new InvalidOperationException(
                $"Missing config 'DownstreamApis:{downstreamName}:AppPermissionScopes'.");
}
