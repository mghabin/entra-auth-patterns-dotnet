using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Ftgo.Orders.Api.Controllers;

[ApiController]
[Route("api/[controller]")]
public sealed class OrdersController : ControllerBase
{
    /// <summary>User-token endpoint. Bound to the <see cref="OrdersAuthorizationPolicies.Delegated"/> named policy.</summary>
    [HttpGet("whoami")]
    [Authorize(Policy = OrdersAuthorizationPolicies.Delegated)]
    public IActionResult WhoAmI() => Ok(new
    {
        service = "Ftgo.Orders.Api",
        flow = "user (OBO)",
        oid = User.FindFirst("oid")?.Value,
        tid = User.FindFirst("tid")?.Value,
        scp = User.FindFirst("scp")?.Value,
        name = User.FindFirst("name")?.Value
    });

    /// <summary>App-only endpoint. Bound to the <see cref="OrdersAuthorizationPolicies.App"/> named policy.</summary>
    [HttpGet("system")]
    [Authorize(Policy = OrdersAuthorizationPolicies.App)]
    public IActionResult System() => Ok(new
    {
        service = "Ftgo.Orders.Api",
        flow = "app (S2S)",
        azp = User.FindFirst("azp")?.Value ?? User.FindFirst("appid")?.Value,
        roles = User.FindAll("roles").Select(c => c.Value).ToArray(),
        idtyp = User.FindFirst("idtyp")?.Value
    });
}
