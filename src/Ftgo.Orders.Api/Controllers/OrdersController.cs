using Ftgo.Auth;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Identity.Web;
using Microsoft.Identity.Web.Resource;

namespace Ftgo.Orders.Api.Controllers;

[ApiController]
[Route("api/[controller]")]
[Authorize]
public sealed class OrdersController : ControllerBase
{
    /// <summary>User-token endpoint.</summary>
    [HttpGet("whoami")]
    [RequiredScope("orders.read")]
    public IActionResult WhoAmI() => Ok(new
    {
        service = "Ftgo.Orders.Api",
        flow = "user (OBO)",
        oid = User.FindFirst("oid")?.Value,
        tid = User.FindFirst("tid")?.Value,
        scp = User.FindFirst("scp")?.Value,
        name = User.FindFirst("name")?.Value
    });

    /// <summary>App-only endpoint: requires the <c>Orders.Process</c> role and an allow-listed caller app.</summary>
    [HttpGet("system")]
    [Authorize(Roles = "Orders.Process")]
    [RequireClientApp]
    public IActionResult System() => Ok(new
    {
        service = "Ftgo.Orders.Api",
        flow = "app (S2S)",
        azp = User.FindFirst("azp")?.Value ?? User.FindFirst("appid")?.Value,
        roles = User.FindAll("roles").Select(c => c.Value).ToArray(),
        idtyp = User.FindFirst("idtyp")?.Value
    });
}
