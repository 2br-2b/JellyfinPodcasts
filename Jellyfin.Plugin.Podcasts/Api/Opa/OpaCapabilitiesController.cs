using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Jellyfin.Plugin.Podcasts.Api.Opa;

/// <summary>
/// OPA capabilities endpoint — advertises which OPA features this server supports.
/// </summary>
[ApiController]
[Route("podcasts/sync/opa")]
public class OpaCapabilitiesController : ControllerBase
{
    [HttpGet("capabilities")]
    [AllowAnonymous]
    public IActionResult GetCapabilities()
    {
        return Ok(new
        {
            version = "1.0",
            features = new
            {
                subscriptions = true,
                episodes = true
            }
        });
    }
}
