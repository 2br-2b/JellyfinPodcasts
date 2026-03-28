using System;
using System.Threading;
using System.Threading.Tasks;
using Jellyfin.Plugin.Template.Api.Auth;
using Jellyfin.Plugin.Template.Api.Models;
using Jellyfin.Plugin.Template.Services;
using Microsoft.AspNetCore.Mvc;

namespace Jellyfin.Plugin.Template.Api.Controllers;

/// <summary>
/// Open Podcast API deletion status endpoints.
/// </summary>
[ApiController]
[Route("v1/deletions")]
[ServiceFilter(typeof(PodcastAuthorizationFilter))]
[Produces("application/json")]
public class DeletionsController : ControllerBase
{
    private readonly ISubscriptionStore _subscriptionStore;

    /// <summary>Initializes a new instance of the <see cref="DeletionsController"/> class.</summary>
    /// <param name="subscriptionStore">The subscription store.</param>
    public DeletionsController(ISubscriptionStore subscriptionStore)
    {
        _subscriptionStore = subscriptionStore;
    }

    /// <summary>Gets the status of a prior deletion request.</summary>
    /// <param name="id">The deletion identifier.</param>
    /// <param name="cancellationToken">The request cancellation token.</param>
    /// <returns>The deletion status response.</returns>
    [HttpGet("{id:int}")]
    [ProducesResponseType(typeof(DeletionStatusResponse), 200)]
    [ProducesResponseType(404)]
    public async Task<ActionResult<DeletionStatusResponse>> GetAsync(int id, CancellationToken cancellationToken)
    {
        var userId = HttpContext.Items.TryGetValue(PodcastAuthorizationFilter.UserIdKey, out var value) && value is Guid guid
            ? guid
            : throw new InvalidOperationException("Podcast API request did not include an authenticated user.");

        var deletion = await _subscriptionStore.GetDeletionRequestAsync(userId, id, cancellationToken)
            .ConfigureAwait(false);
        if (deletion is null)
        {
            return NotFound(new { code = 404, message = "Deletion not found" });
        }

        return Ok(new DeletionStatusResponse
        {
            DeletionId = deletion.Id,
            Status = deletion.Status,
            Message = deletion.Message,
        });
    }
}
