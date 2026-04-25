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
    [HttpGet("via-obo")]
    [RequiredScope("orders.read")]
    public async Task<IActionResult> ViaObo()
    {
        // Microsoft.Identity.Web performs the OBO exchange transparently inside CallApiForUserAsync.
        var resp = await downstream.CallApiForUserAsync("Orders", o => o.RelativePath = "api/orders/whoami");
        var body = await resp.Content.ReadAsStringAsync();
        return Content(body, "application/json");
    }

    [HttpGet("via-s2s")]
    [RequiredScope("orders.read")]
    public async Task<IActionResult> ViaS2S()
    {
        var resp = await downstream.CallApiForAppAsync("Orders", o => o.RelativePath = "api/orders/system");
        var body = await resp.Content.ReadAsStringAsync();
        return Content(body, "application/json");
    }

    [HttpGet("via-s2s-multitenant")]
    [RequiredScope("orders.read")]
    public async Task<IActionResult> ViaS2SMultiTenant()
    {
        var resp = await downstream.CallApiForAppAsync("Restaurants", o => o.RelativePath = "api/restaurants/system");
        var body = await resp.Content.ReadAsStringAsync();
        return Content(body, "application/json");
    }
}
