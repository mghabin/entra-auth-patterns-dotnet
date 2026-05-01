using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Ftgo.Restaurants.Api.Controllers;

[ApiController]
[Route("api/[controller]")]
public sealed class RestaurantsController : ControllerBase
{
    /// <summary>App-only multi-tenant endpoint. Bound to the <see cref="RestaurantsAuthorizationPolicies.App"/> named policy.</summary>
    [HttpGet("system")]
    [Authorize(Policy = RestaurantsAuthorizationPolicies.App)]
    public IActionResult System() => Ok(new
    {
        service = "Ftgo.Restaurants.Api",
        flow = "app (S2S, multi-tenant)",
        callerTenant = User.FindFirst("tid")?.Value,
        azp = User.FindFirst("azp")?.Value ?? User.FindFirst("appid")?.Value,
        roles = User.FindAll("roles").Select(c => c.Value).ToArray()
    });
}
