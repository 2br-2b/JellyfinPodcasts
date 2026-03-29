using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Jellyfin.Plugin.Podcasts.FeedParser;
using Jellyfin.Plugin.Template.Api.Auth;
using Jellyfin.Plugin.Template.Api.Models;
using Jellyfin.Plugin.Template.Models;
using Jellyfin.Plugin.Template.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Jellyfin.Plugin.Template.Api.Controllers;

/// <summary>
/// Open Podcast API subscription endpoints.
/// </summary>
[ApiController]
[Route("v1/subscriptions")]
[ServiceFilter(typeof(PodcastAuthorizationFilter))]
[Produces("application/json")]
public class SubscriptionsController : ControllerBase
{
    private readonly IServiceScopeFactory _serviceScopeFactory;
    private readonly ISubscriptionStore _subscriptionStore;
    private readonly ILogger<SubscriptionsController> _logger;

    /// <summary>Initializes a new instance of the <see cref="SubscriptionsController"/> class.</summary>
    /// <param name="serviceScopeFactory">Factory for creating background work scopes.</param>
    /// <param name="subscriptionStore">The subscription store.</param>
    /// <param name="logger">The controller logger.</param>
    public SubscriptionsController(
        IServiceScopeFactory serviceScopeFactory,
        ISubscriptionStore subscriptionStore,
        ILogger<SubscriptionsController> logger)
    {
        _serviceScopeFactory = serviceScopeFactory;
        _subscriptionStore = subscriptionStore;
        _logger = logger;
    }

    /// <summary>Returns the authenticated user's subscriptions.</summary>
    /// <param name="since">Optional lower bound for returned changes.</param>
    /// <param name="page">Optional 1-based page number.</param>
    /// <param name="perPage">Optional page size.</param>
    /// <param name="cancellationToken">The request cancellation token.</param>
    /// <returns>The paged subscription response.</returns>
    [HttpGet]
    [ProducesResponseType(typeof(SubscriptionListResponse), 200)]
    public async Task<ActionResult<SubscriptionListResponse>> GetAllAsync(
        [FromQuery] DateTime? since,
        [FromQuery(Name = "page")] int? page,
        [FromQuery(Name = "per_page")] int? perPage,
        CancellationToken cancellationToken)
    {
        var userId = GetAuthenticatedUserId();
        var resolvedPage = Math.Max(page ?? 1, 1);
        var resolvedPerPage = Math.Clamp(perPage ?? 50, 1, 200);

        var (items, total) = await _subscriptionStore
            .GetSubscriptionsPagedAsync(userId, since, resolvedPage, resolvedPerPage, cancellationToken)
            .ConfigureAwait(false);

        var totalPages = total == 0 ? 1 : (int)Math.Ceiling(total / (double)resolvedPerPage);
        return Ok(new SubscriptionListResponse
        {
            Total = total,
            Page = resolvedPage,
            PerPage = resolvedPerPage,
            Next = resolvedPage < totalPages ? BuildPageUrl(resolvedPage + 1, resolvedPerPage, since) : null,
            Previous = resolvedPage > 1 ? BuildPageUrl(resolvedPage - 1, resolvedPerPage, since) : null,
            Subscriptions = items.Select(item => MapSubscription(item)).ToList(),
        });
    }

    /// <summary>Adds one or more subscriptions for the authenticated user.</summary>
    /// <param name="request">The subscription batch request body.</param>
    /// <param name="cancellationToken">The request cancellation token.</param>
    /// <returns>The batch success and failure response.</returns>
    [HttpPost]
    [ProducesResponseType(typeof(SubscriptionBatchResponse), 200)]
    public async Task<ActionResult<SubscriptionBatchResponse>> CreateAsync(
        [FromBody] SubscriptionBatchRequest request,
        CancellationToken cancellationToken)
    {
        var userId = GetAuthenticatedUserId();
        var successes = new List<SubscriptionResponse>();
        var failures = new List<SubscriptionFailureResponse>();

        foreach (var item in request.Subscriptions)
        {
            if (!Uri.TryCreate(item.FeedUrl, UriKind.Absolute, out var feedUri)
                || (feedUri.Scheme != Uri.UriSchemeHttp && feedUri.Scheme != Uri.UriSchemeHttps))
            {
                failures.Add(new SubscriptionFailureResponse
                {
                    FeedUrl = item.FeedUrl,
                    Message = "No protocol present",
                });
                continue;
            }

            var guid = !string.IsNullOrWhiteSpace(item.Guid)
                ? item.Guid
                : Guid.NewGuid().ToString();

            var feed = new PodcastFeed
            {
                Id = guid!,
                FeedUrl = item.FeedUrl,
                Title = item.FeedUrl,
                Description = string.Empty,
            };

            var subscription = await _subscriptionStore
                .UpsertSubscriptionAsync(userId, feed, cancellationToken)
                .ConfigureAwait(false);
            successes.Add(MapSubscription(subscription));

            _ = Task.Run(
                () => FetchAndUpdateFeedAsync(userId, item.FeedUrl, subscription.FeedId),
                CancellationToken.None);
        }

        return Ok(new SubscriptionBatchResponse
        {
            Success = successes,
            Failure = failures,
        });
    }

    /// <summary>Returns a single subscription for the authenticated user.</summary>
    /// <param name="guid">The subscription GUID.</param>
    /// <param name="cancellationToken">The request cancellation token.</param>
    /// <returns>The matching subscription response.</returns>
    [HttpGet("{guid}")]
    [ProducesResponseType(typeof(SubscriptionResponse), 200)]
    [ProducesResponseType(410)]
    [ProducesResponseType(404)]
    public async Task<ActionResult<SubscriptionResponse>> GetByGuidAsync(string guid, CancellationToken cancellationToken)
    {
        var userId = GetAuthenticatedUserId();
        var requested = await _subscriptionStore
            .GetUserSubscriptionByGuidAsync(userId, guid, cancellationToken)
            .ConfigureAwait(false);
        if (requested is null)
        {
            return NotFound(new { code = 404, message = "Subscription not found" });
        }

        var latest = await _subscriptionStore
            .GetLatestSubscriptionAsync(userId, guid, cancellationToken)
            .ConfigureAwait(false) ?? requested;
        if (!latest.IsSubscribed && latest.Deleted is not null)
        {
            return StatusCode(410, new { code = 410, message = "Subscription has been deleted" });
        }

        return Ok(MapSubscription(requested, latest));
    }

    /// <summary>Updates a single subscription entry.</summary>
    /// <param name="guid">The subscription GUID.</param>
    /// <param name="request">The patch request body.</param>
    /// <param name="cancellationToken">The request cancellation token.</param>
    /// <returns>The patch summary response.</returns>
    [HttpPatch("{guid}")]
    [ProducesResponseType(typeof(SubscriptionPatchResponse), 200)]
    [ProducesResponseType(400)]
    [ProducesResponseType(404)]
    public async Task<ActionResult<SubscriptionPatchResponse>> PatchAsync(
        string guid,
        [FromBody] SubscriptionPatchRequest request,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(request.NewFeedUrl) && string.IsNullOrWhiteSpace(request.NewGuid) && !request.IsSubscribed.HasValue)
        {
            return BadRequest(new { code = 400, message = "At least one field must be updated" });
        }

        if (!string.IsNullOrWhiteSpace(request.NewFeedUrl)
            && (!Uri.TryCreate(request.NewFeedUrl, UriKind.Absolute, out var newFeedUri)
                || (newFeedUri.Scheme != Uri.UriSchemeHttp && newFeedUri.Scheme != Uri.UriSchemeHttps)))
        {
            return BadRequest(new { code = 400, message = "new_feed_url must be an absolute http(s) URL" });
        }

        var userId = GetAuthenticatedUserId();
        var latest = await _subscriptionStore
            .GetLatestSubscriptionAsync(userId, guid, cancellationToken)
            .ConfigureAwait(false);
        if (latest is null)
        {
            return NotFound(new { code = 404, message = "Subscription not found" });
        }

        var updated = await _subscriptionStore
            .PatchSubscriptionAsync(
                userId,
                latest.FeedId,
                request.NewFeedUrl,
                request.NewGuid,
                request.IsSubscribed,
                cancellationToken)
            .ConfigureAwait(false);
        if (updated is null)
        {
            return NotFound(new { code = 404, message = "Subscription not found" });
        }

        return Ok(new SubscriptionPatchResponse
        {
            NewFeedUrl = request.NewFeedUrl,
            IsSubscribed = request.IsSubscribed,
            SubscriptionChanged = request.IsSubscribed.HasValue ? updated.SubscriptionChanged : null,
            GuidChanged = updated.GuidChanged,
            NewGuid = request.NewGuid,
        });
    }

    /// <summary>Deletes a single subscription entry.</summary>
    /// <param name="guid">The subscription GUID.</param>
    /// <param name="cancellationToken">The request cancellation token.</param>
    /// <returns>The accepted deletion response.</returns>
    [HttpDelete("{guid}")]
    [ProducesResponseType(typeof(DeletionAcceptedResponse), 202)]
    [ProducesResponseType(404)]
    public async Task<ActionResult<DeletionAcceptedResponse>> DeleteAsync(string guid, CancellationToken cancellationToken)
    {
        var userId = GetAuthenticatedUserId();
        var latest = await _subscriptionStore
            .GetLatestSubscriptionAsync(userId, guid, cancellationToken)
            .ConfigureAwait(false);
        if (latest is null)
        {
            return NotFound(new { code = 404, message = "Subscription not found" });
        }

        var deletion = await _subscriptionStore.RequestDeletionAsync(userId, latest.FeedId, cancellationToken)
            .ConfigureAwait(false);
        return Accepted(new DeletionAcceptedResponse
        {
            DeletionId = deletion.Id,
            Message = "Deletion request was received and will be processed",
        });
    }

    private Guid GetAuthenticatedUserId()
        => HttpContext.Items.TryGetValue(PodcastAuthorizationFilter.UserIdKey, out var value) && value is Guid userId
            ? userId
            : throw new InvalidOperationException("Podcast API request did not include an authenticated user.");

    private string BuildPageUrl(int page, int perPage, DateTime? since)
    {
        var queryParts = new List<string> { $"page={page}", $"per_page={perPage}" };
        if (since.HasValue)
        {
            queryParts.Add($"since={Uri.EscapeDataString(since.Value.ToString("O"))}");
        }

        return $"{Request.Path}?{string.Join("&", queryParts)}";
    }

    private static SubscriptionResponse MapSubscription(UserSubscription subscription, UserSubscription? latest = null)
        => new()
        {
            FeedUrl = subscription.Feed.FeedUrl,
            Guid = subscription.FeedId,
            IsSubscribed = subscription.IsSubscribed,
            SubscriptionChanged = subscription.SubscriptionChanged,
            GuidChanged = (latest ?? subscription).GuidChanged,
            NewGuid = latest is not null && latest.FeedId != subscription.FeedId ? latest.FeedId : subscription.NewGuid,
            Deleted = subscription.Deleted,
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
                    },
                    CancellationToken.None)
                .ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to refresh feed metadata for {FeedUrl}", feedUrl);
        }
    }
}
