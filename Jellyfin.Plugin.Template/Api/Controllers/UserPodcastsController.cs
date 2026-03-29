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
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Jellyfin.Plugin.Template.Api.Controllers;

/// <summary>
/// User-scoped podcast settings endpoints for the signed-in Jellyfin user.
/// Prefer the OpenPodcastAPI endpoints for standards-compliant subscription operations wherever possible;
/// this controller exists for Jellyfin-specific UI needs that require extra fields outside the spec.
/// </summary>
[ApiController]
[Authorize]
[Route("Users/Me/Podcasts")]
[Produces("application/json")]
public class UserPodcastsController : ControllerBase
{
    private const string JellyfinUserIdClaim = "Jellyfin-UserId";

    private readonly IServiceScopeFactory _serviceScopeFactory;
    private readonly IAppPasswordStore _appPasswordStore;
    private readonly ISubscriptionStore _subscriptionStore;
    private readonly ILogger<UserPodcastsController> _logger;

    /// <summary>Initializes a new instance of the <see cref="UserPodcastsController"/> class.</summary>
    /// <param name="serviceScopeFactory">Factory for background work scopes.</param>
    /// <param name="appPasswordStore">The app password store.</param>
    /// <param name="subscriptionStore">The subscription store.</param>
    /// <param name="logger">The controller logger.</param>
    public UserPodcastsController(
        IServiceScopeFactory serviceScopeFactory,
        IAppPasswordStore appPasswordStore,
        ISubscriptionStore subscriptionStore,
        ILogger<UserPodcastsController> logger)
    {
        _serviceScopeFactory = serviceScopeFactory;
        _appPasswordStore = appPasswordStore;
        _subscriptionStore = subscriptionStore;
        _logger = logger;
    }

    /// <summary>Gets the current user's podcast subscriptions.</summary>
    /// <param name="cancellationToken">The request cancellation token.</param>
    /// <returns>The current user's subscriptions with Jellyfin UI-only enrichments.</returns>
    [HttpGet("Subscriptions")]
    [ProducesResponseType(typeof(IReadOnlyList<UserPodcastSubscriptionResponse>), StatusCodes.Status200OK)]
    public async Task<ActionResult<IReadOnlyList<UserPodcastSubscriptionResponse>>> GetSubscriptions(CancellationToken cancellationToken)
    {
        var userId = GetCurrentUserId();
        var subscriptions = await _subscriptionStore
            .GetSubscriptionsPagedAsync(userId, null, 1, 500, cancellationToken)
            .ConfigureAwait(false);

        var responses = new List<UserPodcastSubscriptionResponse>(subscriptions.Items.Count);
        foreach (var subscription in subscriptions.Items)
        {
            responses.Add(await MapSubscriptionAsync(subscription, cancellationToken).ConfigureAwait(false));
        }

        return Ok(responses);
    }

    /// <summary>Adds a podcast subscription for the current user.</summary>
    /// <param name="request">The subscription create request.</param>
    /// <param name="cancellationToken">The request cancellation token.</param>
    /// <returns>The resulting subscription.</returns>
    [HttpPost("Subscriptions")]
    [ProducesResponseType(typeof(UserPodcastSubscriptionResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public async Task<ActionResult<UserPodcastSubscriptionResponse>> AddSubscription(
        [FromBody] SubscriptionCreateRequest request,
        CancellationToken cancellationToken)
    {
        if (!Uri.TryCreate(request.FeedUrl, UriKind.Absolute, out var feedUri)
            || (feedUri.Scheme != Uri.UriSchemeHttp && feedUri.Scheme != Uri.UriSchemeHttps))
        {
            return BadRequest(new { code = 400, message = "feed_url must be an absolute http(s) URL" });
        }

        var userId = GetCurrentUserId();
        var parsedFeed = await ParseFeedAsync(request.FeedUrl, cancellationToken).ConfigureAwait(false);
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
            MediaType = InferMediaType(parsedFeed),
        };

        var subscription = await _subscriptionStore
            .UpsertSubscriptionAsync(userId, feed, cancellationToken)
            .ConfigureAwait(false);

        _ = Task.Run(
            () => FetchAndUpdateFeedAsync(userId, request.FeedUrl, subscription.FeedId),
            CancellationToken.None);

        return Ok(await MapSubscriptionAsync(subscription, cancellationToken).ConfigureAwait(false));
    }

    /// <summary>Refreshes a podcast subscription's shared metadata for the current user.</summary>
    /// <param name="guid">The subscription guid.</param>
    /// <param name="cancellationToken">The request cancellation token.</param>
    /// <returns>A no-content response when the refresh was queued.</returns>
    [HttpPost("Subscriptions/{guid}/Refresh")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult> RefreshSubscription(string guid, CancellationToken cancellationToken)
    {
        var userId = GetCurrentUserId();
        var latest = await _subscriptionStore.GetLatestSubscriptionAsync(userId, guid, cancellationToken).ConfigureAwait(false);
        if (latest is null)
        {
            return NotFound(new { code = 404, message = "Subscription not found" });
        }

        _ = Task.Run(
            () => FetchAndUpdateFeedAsync(userId, latest.Feed.FeedUrl, latest.FeedId),
            CancellationToken.None);

        return NoContent();
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

    private async Task<Jellyfin.Plugin.Podcasts.FeedParser.Models.ParsedFeed?> ParseFeedAsync(
        string feedUrl,
        CancellationToken cancellationToken)
    {
        using var scope = _serviceScopeFactory.CreateScope();
        var feedParser = scope.ServiceProvider.GetRequiredService<RssFeedParser>();
        return await feedParser.ParseAsync(feedUrl, cancellationToken).ConfigureAwait(false);
    }

    private static PodcastMediaType InferMediaType(Jellyfin.Plugin.Podcasts.FeedParser.Models.ParsedFeed? parsedFeed)
    {
        if (parsedFeed?.Episodes is null || parsedFeed.Episodes.Count == 0)
        {
            return PodcastMediaType.Audio;
        }

        foreach (var episode in parsedFeed.Episodes)
        {
            var enclosureType = episode.EnclosureType ?? string.Empty;
            if (enclosureType.StartsWith("video/", StringComparison.OrdinalIgnoreCase))
            {
                return PodcastMediaType.Video;
            }

            var audioUrl = episode.AudioUrl ?? string.Empty;
            if (audioUrl.EndsWith(".mp4", StringComparison.OrdinalIgnoreCase)
                || audioUrl.EndsWith(".m4v", StringComparison.OrdinalIgnoreCase)
                || audioUrl.EndsWith(".webm", StringComparison.OrdinalIgnoreCase)
                || audioUrl.EndsWith(".mov", StringComparison.OrdinalIgnoreCase))
            {
                return PodcastMediaType.Video;
            }
        }

        return PodcastMediaType.Audio;
    }

    private Guid GetCurrentUserId()
    {
        var userId = User.FindFirstValue(JellyfinUserIdClaim);
        return Guid.TryParse(userId, out var parsed)
            ? parsed
            : throw new InvalidOperationException("Authenticated user did not include a Jellyfin user id claim.");
    }

    private async Task<UserPodcastSubscriptionResponse> MapSubscriptionAsync(UserSubscription subscription, CancellationToken cancellationToken)
    {
        Jellyfin.Plugin.Podcasts.FeedParser.Models.ParsedFeed? parsedFeed = null;
        try
        {
            parsedFeed = await ParseFeedAsync(subscription.Feed.FeedUrl, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to parse feed {FeedUrl} while loading the user podcasts page", subscription.Feed.FeedUrl);
        }

        // This response intentionally extends the OpenPodcastAPI subscription shape
        // for the Jellyfin user page only. Use the public /v1 OpenPodcastAPI endpoints
        // wherever possible; keep them spec-compliant and reserve this controller for
        // Jellyfin-specific fields such as artwork, media type, and recent episodes.
        return new UserPodcastSubscriptionResponse
        {
            Title = subscription.Feed.Title,
            Description = subscription.Feed.Description,
            ImageUrl = subscription.Feed.ImageUrl,
            HomePageUrl = subscription.Feed.HomePageUrl,
            MediaType = subscription.Feed.MediaType.ToString().ToLowerInvariant(),
            FeedUrl = subscription.Feed.FeedUrl,
            Guid = subscription.FeedId,
            IsSubscribed = subscription.IsSubscribed,
            SubscriptionChanged = subscription.SubscriptionChanged,
            GuidChanged = subscription.GuidChanged,
            NewGuid = subscription.NewGuid,
            Deleted = subscription.Deleted,
            EpisodeCount = parsedFeed?.Episodes?.Count,
            RecentEpisodes = parsedFeed?.Episodes?
                .OrderByDescending(ep => ep.PublishedAt ?? DateTime.MinValue)
                .Take(12)
                .Select(ep => new UserPodcastEpisodeResponse
                {
                    Guid = ep.Guid,
                    Title = ep.Title,
                    MediaUrl = ep.AudioUrl,
                    MimeType = ep.EnclosureType,
                    PublishedAt = ep.PublishedAt,
                    DurationTicks = ep.DurationTicks,
                    ImageUrl = ep.ImageUrl ?? subscription.Feed.ImageUrl,
                })
                .ToList() ?? [],
        };
    }

    private static UserAppPasswordResponse MapAppPassword(AppPassword password)
        => new()
        {
            Id = password.Id,
            Label = password.Label,
            Kind = password.Kind,
            CreatedAt = password.CreatedAt,
            LastUsedAt = password.LastUsedAt,
        };

    private async Task FetchAndUpdateFeedAsync(Guid userId, string feedUrl, string initialFeedId)
    {
        try
        {
            using var scope = _serviceScopeFactory.CreateScope();
            var feedParser = scope.ServiceProvider.GetRequiredService<RssFeedParser>();
            var subscriptionStore = scope.ServiceProvider.GetRequiredService<ISubscriptionStore>();

            var parsedFeed = await feedParser.ParseAsync(feedUrl, CancellationToken.None).ConfigureAwait(false);
            if (parsedFeed is null)
            {
                return;
            }

            var resolvedFeedId = !string.IsNullOrWhiteSpace(parsedFeed.Guid)
                ? parsedFeed.Guid
                : initialFeedId;

            if (!string.Equals(resolvedFeedId, initialFeedId, StringComparison.Ordinal))
            {
                await subscriptionStore
                    .PatchSubscriptionAsync(
                        userId,
                        initialFeedId,
                        null,
                        resolvedFeedId,
                        null,
                        CancellationToken.None)
                    .ConfigureAwait(false);
            }

            await subscriptionStore.UpdateFeedMetadataAsync(
                    resolvedFeedId,
                    new PodcastFeed
                    {
                        Id = resolvedFeedId,
                        FeedUrl = feedUrl,
                        Title = parsedFeed.Title ?? feedUrl,
                        Description = parsedFeed.Description ?? string.Empty,
                        ImageUrl = parsedFeed.ImageUrl,
                        HomePageUrl = parsedFeed.HomePageUrl,
                        MediaType = InferMediaType(parsedFeed),
                    },
                    CancellationToken.None)
                .ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to refresh user podcast metadata for {FeedUrl}", feedUrl);
        }
    }
}
