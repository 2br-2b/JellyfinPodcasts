using System.IO;
using Microsoft.AspNetCore.Mvc;

namespace Jellyfin.Plugin.Template.Api.Controllers;

/// <summary>
/// Serves the podcast management UI to signed-in Jellyfin users without going through
/// the admin-only configuration-page menu APIs.
/// </summary>
[ApiController]
public class PodcastPageController : ControllerBase
{
    private const string UserPodcastsResourcePath = "Jellyfin.Plugin.Template.Configuration.userPodcastsPage.html";

    /// <summary>
    /// Returns the podcast management page for the signed-in user.
    /// </summary>
    /// <returns>The embedded podcasts HTML page.</returns>
    [HttpGet("Podcasts")]
    [HttpGet("web/podcasts")]
    [Produces("text/html")]
    public IActionResult GetUserPodcastsPage()
    {
        Stream? stream = typeof(Plugin).Assembly.GetManifestResourceStream(UserPodcastsResourcePath);
        if (stream is null)
        {
            return NotFound();
        }

        return File(stream, "text/html; charset=utf-8");
    }
}
