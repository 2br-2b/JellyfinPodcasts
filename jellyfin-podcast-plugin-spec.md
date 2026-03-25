# Jellyfin Podcast Plugin — Design Specification

**Status:** Draft  
**Version:** 0.1  
**Target platform:** Jellyfin 10.9+ (.NET 8)

---

## 1. Goals and Non-Goals

### Goals

- Manage podcast RSS feed subscriptions server-side, with all episode metadata visible in the Jellyfin library regardless of local cache state.
- Download episodes to a bounded local disk cache. Evict old episodes automatically (rolling LRU eviction) to stay within a configured quota. Episodes not in the cache are streamed on-demand from their original source URL when played.
- Allow individual episodes to be pinned, exempting them from eviction.
- Synchronize playback progress, played/unplayed state, and subscriptions between the Jellyfin web UI and AntennaPod (Android) via the Open Podcast API (OPA) and the gpodder v2 API as a compatibility shim.
- Support Jellyfin SSO (Authentik / OAuth 2.0 / OIDC) for browser clients; provide app-password generation for clients that cannot participate in OAuth flows (AntennaPod).
- Audio podcasts only. Video (vodcast) support is explicitly out of scope for v1.

### Non-Goals

- Acting as an RSS proxy or rewriting enclosure URLs. Clients subscribe to original feed URLs and download directly from podcast CDNs.
- Replacing Jellyfin's core media library for music or video.
- Providing a podcast discovery service or integrating with Podcast Index.

---

## 2. Architecture Overview

```
┌─────────────────────────────────────────────────────────┐
│                    Jellyfin Server                       │
│                                                          │
│  ┌──────────────┐   ┌─────────────────────────────────┐ │
│  │  Jellyfin    │   │      Podcast Plugin              │ │
│  │  Core        │◄──┤                                  │ │
│  │  (library,   │   │  ┌───────────┐ ┌─────────────┐  │ │
│  │   auth,      │   │  │ Feed      │ │ Cache       │  │ │
│  │   playback)  │   │  │ Manager   │ │ Manager     │  │ │
│  └──────────────┘   │  └─────┬─────┘ └──────┬──────┘  │ │
│                     │        │               │          │ │
│                     │  ┌─────▼───────────────▼──────┐  │ │
│                     │  │       Plugin Database       │  │ │
│                     │  │  (SQLite via EF Core)       │  │ │
│                     │  └────────────────────────────┘  │ │
│                     │                                   │ │
│                     │  ┌────────────────────────────┐  │ │
│                     │  │   Sync API Controllers      │  │ │
│                     │  │  OPA  │  gpodder compat     │  │ │
│                     │  └────────────────────────────┘  │ │
│                     │                                   │ │
│                     │  ┌────────────────────────────┐  │ │
│                     │  │   App Password Manager     │  │ │
│                     │  └────────────────────────────┘  │ │
│                     └─────────────────────────────────┘ │
└─────────────────────────────────────────────────────────┘
         ▲                        ▲
         │  Jellyfin API          │  OPA / gpodder API
    ┌────┴──────┐          ┌──────┴──────┐
    │ Jellyfin  │          │ AntennaPod  │
    │ Web UI    │          │ (Android)   │
    └───────────┘          └─────────────┘
```

The plugin runs entirely inside the Jellyfin process as a standard `IPlugin` / `IPluginServiceRegistrar`. It does not require any sidecar services. Persistent state lives in a SQLite database managed by EF Core, stored in the Jellyfin plugin data directory. The plugin registers:

- Custom `IItemResolver` and `ILibraryManager` hooks to surface podcast episodes as Jellyfin library items.
- `IScheduledTask` instances for feed polling and cache eviction.
- ASP.NET Core controllers (via Jellyfin's `IControllerRouteService`) for the OPA sync endpoints, the gpodder compatibility shim, and app-password management.
- An `IServerEntryPoint` for startup wiring.

---

## 3. Data Model

All tables live in the plugin's own SQLite database (`podcast_plugin.db`).

### 3.1 `podcasts`

| Column | Type | Notes |
|---|---|---|
| `id` | UUID PK | Internal stable identifier |
| `feed_url` | TEXT UNIQUE NOT NULL | Canonical RSS feed URL |
| `guid` | TEXT | Feed-level GUID (from `<podcast:guid>` tag if present, else derived) |
| `title` | TEXT | |
| `description` | TEXT | |
| `image_url` | TEXT | |
| `author` | TEXT | |
| `language` | TEXT | |
| `last_fetched_at` | DATETIME | Last successful RSS fetch |
| `fetch_error` | TEXT NULL | Last fetch error, if any |
| `created_at` | DATETIME | |

### 3.2 `episodes`

| Column | Type | Notes |
|---|---|---|
| `id` | UUID PK | Internal stable identifier |
| `podcast_id` | UUID FK → podcasts | |
| `guid` | TEXT NOT NULL | From `<guid>` RSS element; fallback to enclosure URL |
| `title` | TEXT | |
| `description` | TEXT | |
| `published_at` | DATETIME | `<pubDate>` |
| `duration_seconds` | INTEGER NULL | From RSS `<itunes:duration>` |
| `enclosure_url` | TEXT NOT NULL | Original audio file URL |
| `enclosure_mime` | TEXT | e.g. `audio/mpeg` |
| `enclosure_length` | INTEGER NULL | Bytes, from RSS |
| `image_url` | TEXT NULL | Episode-level image if present |
| `is_cached` | BOOLEAN | True if file is present on disk |
| `cached_at` | DATETIME NULL | When the file was last written to cache |
| `cached_size_bytes` | INTEGER NULL | Actual on-disk size |
| `is_pinned` | BOOLEAN DEFAULT FALSE | Exempt from LRU eviction |
| `local_path` | TEXT NULL | Relative path under cache root |
| `jellyfin_item_id` | TEXT NULL | Jellyfin library item ID once indexed |
| `created_at` | DATETIME | When episode was first seen in feed |

### 3.3 `user_episode_state`

Stores per-user playback state. This is the source of truth for sync.

| Column | Type | Notes |
|---|---|---|
| `id` | UUID PK | |
| `user_id` | TEXT NOT NULL | Jellyfin user ID |
| `episode_id` | UUID FK → episodes | |
| `position_seconds` | INTEGER | Last known playback position |
| `is_played` | BOOLEAN | |
| `updated_at` | DATETIME | Used as the sync cursor |

Unique constraint on `(user_id, episode_id)`.

### 3.4 `user_subscriptions`

| Column | Type | Notes |
|---|---|---|
| `id` | UUID PK | |
| `user_id` | TEXT NOT NULL | Jellyfin user ID |
| `podcast_id` | UUID FK → podcasts | |
| `is_subscribed` | BOOLEAN | Allows soft-unsubscribe with tombstone |
| `subscription_changed_at` | DATETIME | OPA `subscription_changed` field |
| `deleted_at` | DATETIME NULL | OPA tombstone |

### 3.5 `app_passwords`

| Column | Type | Notes |
|---|---|---|
| `id` | UUID PK | |
| `user_id` | TEXT NOT NULL | Jellyfin user ID |
| `label` | TEXT | Human-readable name, e.g. "AntennaPod on Pixel 8" |
| `password_hash` | TEXT | bcrypt |
| `created_at` | DATETIME | |
| `last_used_at` | DATETIME NULL | |

---

## 4. Feed Management

### 4.1 RSS Polling

A `PodcastFeedPollingTask` (implements `IScheduledTask`) runs on a configurable interval (default: 60 minutes). For each podcast in the database, it:

1. Issues a conditional GET (`If-Modified-Since` / `If-None-Match`) to the feed URL.
2. On 200: parses the feed with `System.ServiceModel.Syndication` or `CodeHollow.FeedReader`.
3. For each `<item>`: upserts into `episodes` keyed on `guid` (falls back to enclosure URL if no GUID). Does not delete episodes that disappear from the feed — they remain in the library as historical records.
4. Updates `podcasts.last_fetched_at`.
5. For any newly-discovered episodes: if the rolling cache has headroom, enqueues a background download (see §5).

New subscriptions trigger an immediate out-of-cycle fetch.

### 4.2 Metadata Resolution

The plugin implements a custom `ILibraryManager` integration that presents each podcast as a virtual "Series" and each episode as a virtual "Episode" item in a dedicated library of type `Podcast` (a new library type registered by the plugin). This allows the standard Jellyfin web UI to browse podcasts and episodes.

For episodes where `is_cached = false`, Jellyfin item metadata includes the `enclosure_url` as the media stream source. When a client requests playback of an uncached episode, the plugin intercepts the media info request (see §5.3) and either serves the cached file or proxies/redirects to the origin URL.

---

## 5. Cache Management

### 5.1 Configuration (per-library or global)

| Setting | Default | Description |
|---|---|---|
| `MaxCacheSizeGb` | 20 | Maximum total disk usage for cached episode audio |
| `MaxEpisodesPerPodcast` | null | Optional per-feed cap |
| `DownloadNewEpisodes` | `true` | Auto-download newly discovered episodes |
| `AutoDownloadCount` | 3 | How many of the most recent episodes to auto-download per feed |

### 5.2 Eviction

A `CacheEvictionTask` runs after each feed poll and can be triggered manually. Algorithm:

1. Compute current total cache size from `sum(cached_size_bytes)` where `is_cached = true`.
2. If under quota, stop.
3. Build eviction candidates: episodes where `is_cached = true AND is_pinned = false`, ordered by `cached_at ASC` (oldest-cached first; effectively LRU since re-plays refresh `cached_at`).
4. Delete local files and set `is_cached = false`, `local_path = null`, `cached_at = null` for each candidate until under quota.

Pinned episodes are never evicted regardless of quota pressure.

### 5.3 On-Demand Playback of Uncached Episodes

When Jellyfin's playback pipeline requests the media stream for an uncached episode, a custom `IMediaSourceManager` hook intercepts the request:

1. **Check cache again** (may have been downloaded since page load).
2. **If still uncached:** download the episode synchronously (streaming to disk while also streaming to the client, i.e., a tee-pipe). This provides immediate playback without waiting for a full download. Mark `is_cached = true` once the download completes.
3. **Run eviction** in the background after the download, in case the new file pushed the cache over quota.

The web UI should display a visual indicator (e.g., a cloud/download icon) on episode cards where `is_cached = false`, so the user knows the server will fetch from origin when they press play.

### 5.4 Pinning

Users can pin an episode via:
- The Jellyfin web UI (plugin-provided context menu action: "Pin – keep offline forever").
- The plugin's own management UI (see §7).

Pinning sets `episodes.is_pinned = true`. Unpinning does not evict immediately; the episode remains cached and becomes eligible for future eviction.

---

## 6. Sync API

### 6.1 Authentication

The sync API is mounted under `/podcasts/sync/`. Two authentication paths exist:

**Path A — Jellyfin session token (web UI, trusted clients)**  
Standard `Authorization: MediaBrowser Token="..."` header. The plugin validates this against Jellyfin's `IAuthorizationContext`.

**Path B — App password (AntennaPod and other basic-auth clients)**  
HTTP Basic Auth against the `app_passwords` table. The username is the Jellyfin username; the password is the app password. App passwords are scoped only to the podcast sync API — they cannot be used to access other Jellyfin endpoints.

### 6.2 Open Podcast API (Primary)

The plugin implements the [Open Podcast API](https://openpodcastapi.org) specification. As of the current spec, only the **Subscriptions** endpoint is finalized; the **Episodes** endpoint is in progress. The plugin implements both, treating the Episodes endpoint as "experimental" until the spec stabilises.

#### 6.2.1 Capabilities Endpoint

```
GET /podcasts/sync/opa/capabilities
```

Response:
```json
{
  "version": "1.0",
  "features": {
    "subscriptions": true,
    "episodes": true
  }
}
```

#### 6.2.2 Subscriptions Endpoint (OPA Core)

Implements the full OPA Subscriptions spec:

| Method | Path | Description |
|---|---|---|
| `GET` | `/podcasts/sync/opa/subscriptions` | Get all subscriptions (supports `since` datetime filter) |
| `POST` | `/podcasts/sync/opa/subscriptions` | Add new subscription |
| `GET` | `/podcasts/sync/opa/subscriptions/{guid}` | Get single subscription |
| `PATCH` | `/podcasts/sync/opa/subscriptions/{guid}` | Update subscription (e.g., unsubscribe) |
| `DELETE` | `/podcasts/sync/opa/subscriptions/{guid}` | Delete subscription (tombstoned) |
| `GET` | `/podcasts/sync/opa/subscriptions/deletions` | Get deletion tombstones since datetime |

Subscription objects use the OPA schema:
```json
{
  "feed_url": "https://example.com/feed.rss",
  "guid": "urn:uuid:...",
  "is_subscribed": true,
  "subscription_changed": "2025-03-01T12:00:00Z",
  "guid_changed": "2025-03-01T12:00:00Z",
  "new_guid": null,
  "deleted": null
}
```

The server is authoritative. When AntennaPod POSTs a `feed_url` the plugin doesn't know yet, the plugin fetches the feed immediately (§4.1) and creates the podcast record.

GUID tombstoning (tracking feed URL changes) is implemented as described in the OPA spec.

#### 6.2.3 Episodes Endpoint (OPA, Experimental)

Tracks per-user episode state. The Episode object mirrors `user_episode_state`:

```json
{
  "podcast_guid": "urn:uuid:...",
  "episode_guid": "...",
  "position_seconds": 1234,
  "is_played": false,
  "updated_at": "2025-03-20T18:30:00Z"
}
```

| Method | Path | Description |
|---|---|---|
| `GET` | `/podcasts/sync/opa/episodes` | Get all episode states (supports `since` filter) |
| `POST` | `/podcasts/sync/opa/episodes` | Batch upsert episode states |
| `GET` | `/podcasts/sync/opa/episodes/{podcast_guid}/{episode_guid}` | Get single episode state |
| `PATCH` | `/podcasts/sync/opa/episodes/{podcast_guid}/{episode_guid}` | Update episode state |

Conflict resolution: last-write-wins based on `updated_at`. Clients must send a timestamp with each update.

### 6.3 gpodder v2 Compatibility Shim

AntennaPod supports both OPA and gpodder sync. The gpodder shim exists as a fallback and for compatibility with the current stable release of AntennaPod until OPA support is stable there. It is implemented as a thin adapter layer that maps gpodder protocol calls to the same underlying `user_episode_state` and `user_subscriptions` tables.

```
/podcasts/sync/gpodder/api/2/auth/{username}/login.json    POST
/podcasts/sync/gpodder/api/2/auth/{username}/logout.json   POST
/podcasts/sync/gpodder/api/2/subscriptions/{username}/{deviceid}.json  GET, POST
/podcasts/sync/gpodder/api/2/episodes/{username}.json      GET, POST
```

Authentication uses app passwords (HTTP Basic Auth). The shim does not implement the Devices API (`/api/2/devices/`) in a meaningful way — it accepts requests but returns a single synthetic device. This is compatible with AntennaPod's usage.

### 6.4 Conflict Semantics

Both the OPA endpoint and the gpodder shim apply the same rule: **last-write-wins on `updated_at`**. If a client submits an episode state with an `updated_at` older than what the server holds, the server discards the update and returns the server's current state. This prevents stale mobile state from overwriting more recent progress recorded via the web UI.

---

## 7. App Password Management

Because AntennaPod cannot participate in an OAuth 2.0 / OIDC flow, the plugin provides a mechanism to generate per-device app passwords.

A plugin management page (served as a Jellyfin dashboard plugin page) provides:

- A list of active app passwords with labels and last-used timestamps.
- A "Generate new app password" action that:
  1. Prompts for a human-readable label (e.g., "AntennaPod on Pixel 8").
  2. Generates a cryptographically random 32-character password.
  3. Displays the password **once** (it is not stored in plaintext — only a bcrypt hash is stored).
  4. Shows the exact server URL and credentials to enter into AntennaPod's gpodder sync settings or OPA settings.
- Revoke buttons per password.

App passwords are only valid against the podcast sync API routes (`/podcasts/sync/`). They cannot be used to log into Jellyfin itself or access other Jellyfin API endpoints.

---

## 8. Jellyfin Web UI Integration

### 8.1 Library Experience

The plugin registers a new Jellyfin media type: **Podcast**. A Jellyfin admin creates a "Podcast" library pointing to the plugin's virtual media source (not a real filesystem path). The library displays:

- **Podcast list view:** artwork, title, latest episode date, unplayed count badge.
- **Podcast detail view:** description, episode list sorted by published date (newest first).
- **Episode list:** title, published date, duration, playback progress bar (if partially played), "cached" indicator (local disk icon vs. cloud icon for uncached).

Standard Jellyfin playback controls are used. Progress is written back to `user_episode_state` via a Jellyfin `IPlaybackManager` hook, which also updates the sync state so it propagates to AntennaPod on next sync.

### 8.2 Plugin Management UI

A Jellyfin dashboard page (Vue-based, served via the plugin's static files) provides:

- **Subscriptions tab:** add/remove feed URLs, view all subscriptions, trigger manual refresh.
- **Cache tab:** current disk usage vs quota, list of cached episodes with pin/unpin actions, manual eviction trigger.
- **App Passwords tab:** (see §7).
- **Settings tab:** `MaxCacheSizeGb`, `MaxEpisodesPerPodcast`, poll interval, `AutoDownloadCount`.

---

## 9. Authentication Flow Summary

| Client | Auth method | Scope |
|---|---|---|
| Jellyfin web browser | Jellyfin SSO (Authentik → OAuth/OIDC → Jellyfin session) | Full Jellyfin + podcast sync |
| AntennaPod (OPA) | App password (HTTP Basic) | Podcast sync API only |
| AntennaPod (gpodder) | App password (HTTP Basic) | Podcast sync API only |
| Other gpodder clients | App password (HTTP Basic) | Podcast sync API only |

The plugin does **not** attempt to implement OAuth device-flow or PKCE for AntennaPod. App passwords are the intentional design for clients that cannot do browser-redirect OAuth.

---

## 10. Plugin Structure (C# Project Layout)

```
Jellyfin.Plugin.Podcasts/
├── Plugin.cs                        # IPlugin entry point, service registration
├── Configuration/
│   └── PluginConfiguration.cs       # MaxCacheSizeGb, etc.
├── Database/
│   ├── PodcastDbContext.cs           # EF Core DbContext
│   └── Migrations/
├── Model/
│   ├── Podcast.cs
│   ├── Episode.cs
│   ├── UserEpisodeState.cs
│   ├── UserSubscription.cs
│   └── AppPassword.cs
├── Feed/
│   ├── FeedManager.cs               # RSS fetch + upsert logic
│   └── FeedPollingTask.cs           # IScheduledTask
├── Cache/
│   ├── CacheManager.cs              # Download, evict, pin logic
│   ├── CacheEvictionTask.cs         # IScheduledTask
│   └── OnDemandDownloader.cs        # Tee-pipe for live streaming + caching
├── Library/
│   ├── PodcastLibraryManager.cs     # IItemResolver, virtual item bridge
│   └── PlaybackProgressHook.cs      # IPlaybackManager hook → user_episode_state
├── Api/
│   ├── Opa/
│   │   ├── OpaCapabilitiesController.cs
│   │   ├── OpaSubscriptionsController.cs
│   │   └── OpaEpisodesController.cs
│   ├── Gpodder/
│   │   ├── GpodderAuthController.cs
│   │   ├── GpodderSubscriptionsController.cs
│   │   └── GpodderEpisodesController.cs
│   └── Management/
│       └── AppPasswordController.cs
├── Auth/
│   └── AppPasswordAuthProvider.cs   # IAuthenticationProvider for Basic Auth
└── Web/
    ├── dashboard.html               # Plugin management UI entry point
    └── js/
        └── dashboard.js             # Vue-based management UI
```

---

## 11. Key Dependencies

| Dependency | Purpose |
|---|---|
| `Microsoft.EntityFrameworkCore.Sqlite` | Plugin database |
| `CodeHollow.FeedReader` (or `System.ServiceModel.Syndication`) | RSS parsing |
| `BCrypt.Net-Next` | App password hashing |
| Jellyfin SDK (`Jellyfin.Model`, `MediaBrowser.Controller`, etc.) | Plugin APIs |

All other functionality uses the .NET 8 BCL (HttpClient for feed fetching, System.IO for cache file I/O).

---

## 12. Open Questions and Deferred Items

**OPA Episodes spec stability.** The OPA Episodes endpoint spec is not yet finalized. The plugin should treat it as experimental and be prepared to revise the schema when the spec is ratified. The gpodder shim provides a stable fallback in the interim.

**Multi-user cache.** The current design uses a single shared cache. If user A caches an episode and user B plays the same episode, they share the cached file. Pinning is currently global (not per-user). Per-user pinning can be added later by moving `is_pinned` to `user_episode_state`.

**Feed authentication.** Some private podcast feeds require HTTP Basic Auth or token-bearing URLs. The plugin should eventually support per-feed credential storage (encrypted at rest using Jellyfin's `IEncryptionManager`).

**Push vs. poll.** Feed polling on a fixed interval means new episodes can be delayed by up to the poll interval. WebSub (PubSubHubbub) support would allow instant notification for feeds that advertise a hub; deferred to a later release.

**iOS / other clients.** The gpodder shim also covers other clients (e.g., Podverse, Kasts). No additional work is needed for these, but compatibility testing against each client's quirks is recommended before a stable release.
