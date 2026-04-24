using Ftgo.Auth;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Ftgo.RestaurantService.Controllers;

[ApiController]
[Route("api/[controller]")]
[Authorize(Roles = "Restaurants.Read.All")]
[RequireClientApp]
public sealed class RestaurantsController : ControllerBase
{
    [HttpGet("system")]
    public IActionResult System() => Ok(new
    {
        service = "RestaurantService",
        flow = "app (S2S, multi-tenant)",
        callerTenant = User.FindFirst("tid")?.Value,
        azp = User.FindFirst("azp")?.Value ?? User.FindFirst("appid")?.Value,
        roles = User.FindAll("roles").Select(c => c.Value).ToArray()
    });
}
