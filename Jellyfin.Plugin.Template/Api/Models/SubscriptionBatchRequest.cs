#pragma warning disable SA1402
#pragma warning disable SA1649
#pragma warning disable CA1002
#pragma warning disable CA2227
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Text.Json.Serialization;

namespace Jellyfin.Plugin.Template.Api.Models;

/// <summary>Subscription batch request body.</summary>
public class SubscriptionBatchRequest
{
    /// <summary>Gets or sets the subscriptions to add.</summary>
    [JsonPropertyName("subscriptions")]
    public Collection<SubscriptionCreateRequest> Subscriptions { get; set; } = [];
}

/// <summary>Single subscription create request.</summary>
public class SubscriptionCreateRequest
{
    /// <summary>Gets or sets the RSS feed URL.</summary>
    [JsonPropertyName("feed_url")]
    public string FeedUrl { get; set; } = string.Empty;

    /// <summary>Gets or sets the client-supplied GUID when present.</summary>
    [JsonPropertyName("guid")]
    public string? Guid { get; set; }
}

/// <summary>PATCH request body for an existing subscription.</summary>
public class SubscriptionPatchRequest
{
    /// <summary>Gets or sets the new feed URL.</summary>
    [JsonPropertyName("new_feed_url")]
    public string? NewFeedUrl { get; set; }

    /// <summary>Gets or sets the new GUID.</summary>
    [JsonPropertyName("new_guid")]
    public string? NewGuid { get; set; }

    /// <summary>Gets or sets a value indicating whether the user is subscribed.</summary>
    [JsonPropertyName("is_subscribed")]
    public bool? IsSubscribed { get; set; }
}

/// <summary>Single subscription response model.</summary>
public class SubscriptionResponse
{
    /// <summary>Gets or sets the RSS feed URL.</summary>
    [JsonPropertyName("feed_url")]
    public string FeedUrl { get; set; } = string.Empty;

    /// <summary>Gets or sets the subscription GUID.</summary>
    [JsonPropertyName("guid")]
    public string Guid { get; set; } = string.Empty;

    /// <summary>Gets or sets a value indicating whether the user is subscribed.</summary>
    [JsonPropertyName("is_subscribed")]
    public bool IsSubscribed { get; set; }

    /// <summary>Gets or sets the latest subscription change timestamp.</summary>
    [JsonPropertyName("subscription_changed")]
    public DateTime? SubscriptionChanged { get; set; }

    /// <summary>Gets or sets the GUID change timestamp.</summary>
    [JsonPropertyName("guid_changed")]
    public DateTime? GuidChanged { get; set; }

    /// <summary>Gets or sets the latest GUID in the chain.</summary>
    [JsonPropertyName("new_guid")]
    public string? NewGuid { get; set; }

    /// <summary>Gets or sets the deletion timestamp.</summary>
    [JsonPropertyName("deleted")]
    public DateTime? Deleted { get; set; }
}

/// <summary>Paged subscription list response.</summary>
public class SubscriptionListResponse
{
    /// <summary>Gets or sets the total number of matching subscriptions.</summary>
    [JsonPropertyName("total")]
    public int Total { get; set; }

    /// <summary>Gets or sets the current page.</summary>
    [JsonPropertyName("page")]
    public int Page { get; set; }

    /// <summary>Gets or sets the page size.</summary>
    [JsonPropertyName("per_page")]
    public int PerPage { get; set; }

    /// <summary>Gets or sets the next page URL.</summary>
    [JsonPropertyName("next")]
    public string? Next { get; set; }

    /// <summary>Gets or sets the previous page URL.</summary>
    [JsonPropertyName("previous")]
    public string? Previous { get; set; }

    /// <summary>Gets or sets the returned subscriptions.</summary>
    [JsonPropertyName("subscriptions")]
    public IReadOnlyList<SubscriptionResponse> Subscriptions { get; set; } = [];
}

/// <summary>Batch add response payload.</summary>
public class SubscriptionBatchResponse
{
    /// <summary>Gets or sets successfully processed subscriptions.</summary>
    [JsonPropertyName("success")]
    public IReadOnlyList<SubscriptionResponse> Success { get; set; } = [];

    /// <summary>Gets or sets failures from the request payload.</summary>
    [JsonPropertyName("failure")]
    public IReadOnlyList<SubscriptionFailureResponse> Failure { get; set; } = [];
}

/// <summary>Batch add failure response item.</summary>
public class SubscriptionFailureResponse
{
    /// <summary>Gets or sets the RSS feed URL.</summary>
    [JsonPropertyName("feed_url")]
    public string FeedUrl { get; set; } = string.Empty;

    /// <summary>Gets or sets the failure message.</summary>
    [JsonPropertyName("message")]
    public string Message { get; set; } = string.Empty;
}

/// <summary>PATCH response payload.</summary>
public class SubscriptionPatchResponse
{
    /// <summary>Gets or sets the new feed URL.</summary>
    [JsonPropertyName("new_feed_url")]
    public string? NewFeedUrl { get; set; }

    /// <summary>Gets or sets whether the user is subscribed.</summary>
    [JsonPropertyName("is_subscribed")]
    public bool? IsSubscribed { get; set; }

    /// <summary>Gets or sets the latest subscription change timestamp.</summary>
    [JsonPropertyName("subscription_changed")]
    public DateTime? SubscriptionChanged { get; set; }

    /// <summary>Gets or sets the GUID change timestamp.</summary>
    [JsonPropertyName("guid_changed")]
    public DateTime? GuidChanged { get; set; }

    /// <summary>Gets or sets the latest GUID in the chain.</summary>
    [JsonPropertyName("new_guid")]
    public string? NewGuid { get; set; }
}

/// <summary>Deletion accepted response payload.</summary>
public class DeletionAcceptedResponse
{
    /// <summary>Gets or sets the deletion identifier.</summary>
    [JsonPropertyName("deletion_id")]
    public int DeletionId { get; set; }

    /// <summary>Gets or sets the response message.</summary>
    [JsonPropertyName("message")]
    public string Message { get; set; } = string.Empty;
}

/// <summary>Deletion status response payload.</summary>
public class DeletionStatusResponse
{
    /// <summary>Gets or sets the deletion identifier.</summary>
    [JsonPropertyName("deletion_id")]
    public int DeletionId { get; set; }

    /// <summary>Gets or sets the deletion status.</summary>
    [JsonPropertyName("status")]
    public string Status { get; set; } = string.Empty;

    /// <summary>Gets or sets the status message.</summary>
    [JsonPropertyName("message")]
    public string? Message { get; set; }
}

/// <summary>User app password create request.</summary>
public class UserAppPasswordCreateRequest
{
    /// <summary>Gets or sets the app password label.</summary>
    [JsonPropertyName("label")]
    public string Label { get; set; } = string.Empty;

    /// <summary>Gets or sets the app password kind.</summary>
    [JsonPropertyName("kind")]
    public string Kind { get; set; } = string.Empty;
}

/// <summary>User app password response.</summary>
public class UserAppPasswordResponse
{
    /// <summary>Gets or sets the app password identifier.</summary>
    [JsonPropertyName("id")]
    public Guid Id { get; set; }

    /// <summary>Gets or sets the app password label.</summary>
    [JsonPropertyName("label")]
    public string Label { get; set; } = string.Empty;

    /// <summary>Gets or sets the app password kind.</summary>
    [JsonPropertyName("kind")]
    public string Kind { get; set; } = string.Empty;

    /// <summary>Gets or sets when the password was created.</summary>
    [JsonPropertyName("created_at")]
    public DateTime CreatedAt { get; set; }

    /// <summary>Gets or sets when the password was last used.</summary>
    [JsonPropertyName("last_used_at")]
    public DateTime? LastUsedAt { get; set; }
}

/// <summary>User app password create response.</summary>
public class UserAppPasswordCreateResponse
{
    /// <summary>Gets or sets the plaintext token.</summary>
    [JsonPropertyName("token")]
    public string Token { get; set; } = string.Empty;

    /// <summary>Gets or sets the created app password.</summary>
    [JsonPropertyName("password")]
    public UserAppPasswordResponse Password { get; set; } = new();
}
#pragma warning restore CA2227
#pragma warning restore CA1002
#pragma warning restore SA1649
#pragma warning restore SA1402
