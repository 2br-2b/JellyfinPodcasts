using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Claims;
using System.Threading;
using System.Threading.Tasks;
using Jellyfin.Plugin.Podcasts.FeedParser;
using Jellyfin.Plugin.Template.Api.Models;
using Jellyfin.Plugin.Template.Models;
using Jellyfin.Plugin.Template.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;

namespace Jellyfin.Plugin.Template.Api.Controllers;

/// <summary>User-scoped podcast settings endpoints for the signed-in Jellyfin user.</summary>
[ApiController]
[Authorize]
[Route("Users/Me/Podcasts")]
[Produces("application/json")]
public class UserPodcastsController : ControllerBase
{
    private const string JellyfinUserIdClaim = "Jellyfin-UserId";

    private readonly RssFeedParser _feedParser;
    private readonly IAppPasswordStore _appPasswordStore;
    private readonly ISubscriptionStore _subscriptionStore;

    /// <summary>Initializes a new instance of the <see cref="UserPodcastsController"/> class.</summary>
    /// <param name="feedParser">The RSS feed parser.</param>
    /// <param name="appPasswordStore">The app password store.</param>
    /// <param name="subscriptionStore">The subscription store.</param>
    public UserPodcastsController(
        RssFeedParser feedParser,
        IAppPasswordStore appPasswordStore,
        ISubscriptionStore subscriptionStore)
    {
        _feedParser = feedParser;
        _appPasswordStore = appPasswordStore;
        _subscriptionStore = subscriptionStore;
    }

    /// <summary>Gets the current user's podcast subscriptions.</summary>
    /// <param name="cancellationToken">The request cancellation token.</param>
    /// <returns>The current user's subscriptions.</returns>
    [HttpGet("Subscriptions")]
    [ProducesResponseType(typeof(IReadOnlyList<SubscriptionResponse>), StatusCodes.Status200OK)]
    public async Task<ActionResult<IReadOnlyList<SubscriptionResponse>>> GetSubscriptions(CancellationToken cancellationToken)
    {
        var userId = GetCurrentUserId();
        var subscriptions = await _subscriptionStore
            .GetSubscriptionsPagedAsync(userId, null, 1, 500, cancellationToken)
            .ConfigureAwait(false);

        return Ok(subscriptions.Items.Select(MapSubscription).ToList());
    }

    /// <summary>Adds a podcast subscription for the current user.</summary>
    /// <param name="request">The subscription create request.</param>
    /// <param name="cancellationToken">The request cancellation token.</param>
    /// <returns>The resulting subscription.</returns>
    [HttpPost("Subscriptions")]
    [ProducesResponseType(typeof(SubscriptionResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public async Task<ActionResult<SubscriptionResponse>> AddSubscription(
        [FromBody] SubscriptionCreateRequest request,
        CancellationToken cancellationToken)
    {
        if (!Uri.TryCreate(request.FeedUrl, UriKind.Absolute, out var feedUri)
            || (feedUri.Scheme != Uri.UriSchemeHttp && feedUri.Scheme != Uri.UriSchemeHttps))
        {
            return BadRequest(new { code = 400, message = "feed_url must be an absolute http(s) URL" });
        }

        var parsedFeed = await _feedParser.ParseAsync(request.FeedUrl, cancellationToken).ConfigureAwait(false);
        var guid = !string.IsNullOrWhiteSpace(request.Guid)
            ? request.Guid
            : !string.IsNullOrWhiteSpace(parsedFeed?.Guid)
                ? parsedFeed.Guid
                : Guid.NewGuid().ToString();

        var feed = new PodcastFeed
        {
            Id = guid!,
            FeedUrl = request.FeedUrl,
            Title = parsedFeed?.Title ?? request.FeedUrl,
            Description = parsedFeed?.Description ?? string.Empty,
            ImageUrl = parsedFeed?.ImageUrl,
            HomePageUrl = parsedFeed?.HomePageUrl,
        };

        var subscription = await _subscriptionStore
            .UpsertSubscriptionAsync(GetCurrentUserId(), feed, cancellationToken)
            .ConfigureAwait(false);
        return Ok(MapSubscription(subscription));
    }

    /// <summary>Removes a podcast subscription for the current user.</summary>
    /// <param name="guid">The subscription guid.</param>
    /// <param name="cancellationToken">The request cancellation token.</param>
    /// <returns>A no-content response when successful.</returns>
    [HttpDelete("Subscriptions/{guid}")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult> DeleteSubscription(string guid, CancellationToken cancellationToken)
    {
        var userId = GetCurrentUserId();
        var latest = await _subscriptionStore.GetLatestSubscriptionAsync(userId, guid, cancellationToken).ConfigureAwait(false);
        if (latest is null)
        {
            return NotFound(new { code = 404, message = "Subscription not found" });
        }

        await _subscriptionStore.RequestDeletionAsync(userId, latest.FeedId, cancellationToken).ConfigureAwait(false);
        return NoContent();
    }

    /// <summary>Gets the current user's podcast app passwords.</summary>
    /// <param name="cancellationToken">The request cancellation token.</param>
    /// <returns>The current user's app passwords.</returns>
    [HttpGet("AppPasswords")]
    [ProducesResponseType(typeof(IReadOnlyList<UserAppPasswordResponse>), StatusCodes.Status200OK)]
    public async Task<ActionResult<IReadOnlyList<UserAppPasswordResponse>>> GetAppPasswords(CancellationToken cancellationToken)
    {
        var passwords = await _appPasswordStore.GetForUserAsync(GetCurrentUserId(), cancellationToken).ConfigureAwait(false);
        return Ok(passwords.Select(MapAppPassword).ToList());
    }

    /// <summary>Creates a new podcast app password for the current user.</summary>
    /// <param name="request">The app password create request.</param>
    /// <param name="cancellationToken">The request cancellation token.</param>
    /// <returns>The created password plus its one-time token.</returns>
    [HttpPost("AppPasswords")]
    [ProducesResponseType(typeof(UserAppPasswordCreateResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public async Task<ActionResult<UserAppPasswordCreateResponse>> CreateAppPassword(
        [FromBody] UserAppPasswordCreateRequest request,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(request.Label))
        {
            return BadRequest(new { code = 400, message = "label is required" });
        }

        if (!IsSupportedAppPasswordKind(request.Kind))
        {
            return BadRequest(new { code = 400, message = "kind must be openpodcastapi or gpodder" });
        }

        var created = await _appPasswordStore
            .CreateAsync(GetCurrentUserId(), request.Label.Trim(), request.Kind.Trim().ToLowerInvariant(), cancellationToken)
            .ConfigureAwait(false);

        return Ok(new UserAppPasswordCreateResponse
        {
            Token = created.Token,
            Password = MapAppPassword(created.Password),
        });
    }

    /// <summary>Deletes a podcast app password for the current user.</summary>
    /// <param name="id">The app password id.</param>
    /// <param name="cancellationToken">The request cancellation token.</param>
    /// <returns>A no-content response when successful.</returns>
    [HttpDelete("AppPasswords/{id:guid}")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult> DeleteAppPassword(Guid id, CancellationToken cancellationToken)
    {
        var deleted = await _appPasswordStore.DeleteAsync(GetCurrentUserId(), id, cancellationToken).ConfigureAwait(false);
        return deleted ? NoContent() : NotFound(new { code = 404, message = "App password not found" });
    }

    private static bool IsSupportedAppPasswordKind(string? kind)
        => string.Equals(kind, AppPasswordKinds.OpenPodcastApi, StringComparison.OrdinalIgnoreCase)
           || string.Equals(kind, AppPasswordKinds.GPodder, StringComparison.OrdinalIgnoreCase);

    private Guid GetCurrentUserId()
    {
        var userId = User.FindFirstValue(JellyfinUserIdClaim);
        return Guid.TryParse(userId, out var parsed)
            ? parsed
            : throw new InvalidOperationException("Authenticated user did not include a Jellyfin user id claim.");
    }

    private static SubscriptionResponse MapSubscription(UserSubscription subscription)
        => new()
        {
            FeedUrl = subscription.Feed.FeedUrl,
            Guid = subscription.FeedId,
            IsSubscribed = subscription.IsSubscribed,
            SubscriptionChanged = subscription.SubscriptionChanged,
            GuidChanged = subscription.GuidChanged,
            NewGuid = subscription.NewGuid,
            Deleted = subscription.Deleted,
        };

    private static UserAppPasswordResponse MapAppPassword(AppPassword password)
        => new()
        {
            Id = password.Id,
            Label = password.Label,
            Kind = password.Kind,
            CreatedAt = password.CreatedAt,
            LastUsedAt = password.LastUsedAt,
        };
}
