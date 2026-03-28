#pragma warning disable SA1402
using System;

namespace Jellyfin.Plugin.Template.Models;

/// <summary>Tracks an asynchronous subscription deletion request.</summary>
public class DeletionRequest
{
    /// <summary>Gets or sets the auto-incremented deletion identifier.</summary>
    public int Id { get; set; }

    /// <summary>Gets or sets the user who requested the deletion.</summary>
    public Guid UserId { get; set; }

    /// <summary>Gets or sets the feed GUID being deleted.</summary>
    public string FeedId { get; set; } = string.Empty;

    /// <summary>Gets or sets the status string: PENDING, SUCCESS, or FAILURE.</summary>
    public string Status { get; set; } = DeletionStatuses.Pending;

    /// <summary>Gets or sets an optional status message.</summary>
    public string? Message { get; set; }

    /// <summary>Gets or sets when the deletion was requested.</summary>
    public DateTime RequestedAt { get; set; } = DateTime.UtcNow;

    /// <summary>Gets or sets when the deletion completed, or null if still pending.</summary>
    public DateTime? CompletedAt { get; set; }
}

/// <summary>Status string constants matching the OpenPodcastAPI spec.</summary>
public static class DeletionStatuses
{
    /// <summary>Deletion still in progress.</summary>
    public const string Pending = "PENDING";

    /// <summary>Deletion completed successfully.</summary>
    public const string Success = "SUCCESS";

    /// <summary>Deletion failed and was rolled back.</summary>
    public const string Failure = "FAILURE";
}
#pragma warning restore SA1402
