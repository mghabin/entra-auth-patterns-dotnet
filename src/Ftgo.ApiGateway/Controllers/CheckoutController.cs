using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Identity.Abstractions;
using Microsoft.Identity.Web;
using Microsoft.Identity.Web.Resource;

namespace Ftgo.ApiGateway.Controllers;

[ApiController]
[Route("api/[controller]")]
[Authorize]
public sealed class CheckoutController(IDownstreamApi downstream) : ControllerBase
{
    /// <summary>
    /// User → ApiGateway → OBO → OrderService.
    /// Requires the inbound user token to carry scp=orders.read.
    /// </summary>
    [HttpGet("via-obo")]
    [RequiredScope("orders.read")]
    public async Task<IActionResult> ViaObo()
    {
        // Microsoft.Identity.Web does the OBO exchange transparently.
        var resp = await downstream.CallApiForUserAsync("Orders", o => o.RelativePath = "api/orders/whoami");
        var body = await resp.Content.ReadAsStringAsync();
        return Content(body, "application/json");
    }

    /// <summary>
    /// ApiGateway → OrderService as ApiGateway itself (S2S app token, client_credentials).
    /// Use this when there is no user in the request (background refresh, system call).
    /// </summary>
    [HttpGet("via-s2s")]
    [RequiredScope("orders.read")]
    public async Task<IActionResult> ViaS2S()
    {
        var resp = await downstream.CallApiForAppAsync("Orders", o => o.RelativePath = "api/orders/system");
        var body = await resp.Content.ReadAsStringAsync();
        return Content(body, "application/json");
    }

    /// <summary>
    /// ApiGateway → RestaurantService (multi-tenant, app-only) as ApiGateway itself.
    /// </summary>
    [HttpGet("via-s2s-multitenant")]
    [RequiredScope("orders.read")]
    public async Task<IActionResult> ViaS2SMultiTenant()
    {
        var resp = await downstream.CallApiForAppAsync("Restaurants", o => o.RelativePath = "api/restaurants/system");
        var body = await resp.Content.ReadAsStringAsync();
        return Content(body, "application/json");
    }
}
