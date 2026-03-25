# Jellyfin Podcast Plugin — Implementation Todo

Generated from `jellyfin-podcast-plugin-spec.md`.

## Project Setup
- [ ] Create `.NET 8` class library project (`Jellyfin.Plugin.Podcasts.csproj`)
- [ ] Add NuGet dependencies: `MediaBrowser.Controller`, `Microsoft.EntityFrameworkCore.Sqlite`, `CodeHollow.FeedReader`, `BCrypt.Net-Next`
- [ ] Create solution file

## Data Model (§3)
- [ ] `Model/Podcast.cs` — `podcasts` table entity
- [ ] `Model/Episode.cs` — `episodes` table entity
- [ ] `Model/UserEpisodeState.cs` — `user_episode_state` table entity
- [ ] `Model/UserSubscription.cs` — `user_subscriptions` table entity
- [ ] `Model/AppPassword.cs` — `app_passwords` table entity

## Database (§3)
- [ ] `Database/PodcastDbContext.cs` — EF Core DbContext with all 5 tables
- [ ] `Database/Migrations/` — Initial migration

## Plugin Entry Point (§2, §10)
- [ ] `Plugin.cs` — `IPlugin` / `IPluginServiceRegistrar` implementation; registers all services, controllers, tasks
- [ ] `Configuration/PluginConfiguration.cs` — `MaxCacheSizeGb`, `MaxEpisodesPerPodcast`, `DownloadNewEpisodes`, `AutoDownloadCount`, poll interval

## Feed Management (§4)
- [ ] `Feed/FeedManager.cs` — conditional GET, RSS parse with CodeHollow.FeedReader, episode upsert (keyed on GUID → enclosure URL fallback), immediate fetch on new subscription
- [ ] `Feed/FeedPollingTask.cs` — `IScheduledTask`, configurable interval (default 60 min), calls `FeedManager`, enqueues auto-downloads

## Cache Management (§5)
- [ ] `Cache/CacheManager.cs` — download episodes, pin/unpin, eviction algorithm (LRU on `cached_at`, skip pinned), quota check
- [ ] `Cache/CacheEvictionTask.cs` — `IScheduledTask`, runs after poll and on-demand
- [ ] `Cache/OnDemandDownloader.cs` — tee-pipe: stream to disk and to HTTP client simultaneously for uncached playback

## Library Integration (§4.2, §8.1)
- [ ] `Library/PodcastLibraryManager.cs` — `IItemResolver` + virtual library bridge; podcast → virtual Series, episode → virtual Episode; uncached episodes get `enclosure_url` as media source
- [ ] `Library/PlaybackProgressHook.cs` — `IPlaybackManager` hook writes position/played state back to `user_episode_state`

## Sync API — OPA (§6.2)
- [ ] `Api/Opa/OpaCapabilitiesController.cs` — `GET /podcasts/sync/opa/capabilities`
- [ ] `Api/Opa/OpaSubscriptionsController.cs` — full OPA Subscriptions spec (GET list, POST, GET single, PATCH, DELETE, GET deletions)
- [ ] `Api/Opa/OpaEpisodesController.cs` — experimental OPA Episodes spec (GET list, POST batch, GET single, PATCH); last-write-wins on `updated_at`

## Sync API — gpodder v2 Shim (§6.3)
- [ ] `Api/Gpodder/GpodderAuthController.cs` — `POST .../login.json`, `POST .../logout.json`
- [ ] `Api/Gpodder/GpodderSubscriptionsController.cs` — `GET/POST .../subscriptions/{username}/{deviceid}.json`
- [ ] `Api/Gpodder/GpodderEpisodesController.cs` — `GET/POST .../episodes/{username}.json`

## App Password Management (§7)
- [ ] `Api/Management/AppPasswordController.cs` — list, generate (32-char random, bcrypt hash, show-once), revoke
- [ ] `Auth/AppPasswordAuthProvider.cs` — HTTP Basic Auth validator against `app_passwords` table; scoped to `/podcasts/sync/` routes only

## Web UI (§8.2)
- [ ] `Web/dashboard.html` — plugin management page entry point
- [ ] `Web/js/dashboard.js` — Vue-based UI with four tabs:
  - **Subscriptions:** add/remove feeds, manual refresh
  - **Cache:** disk usage, cached episode list, pin/unpin, manual eviction
  - **App Passwords:** list, generate, revoke
  - **Settings:** MaxCacheSizeGb, MaxEpisodesPerPodcast, poll interval, AutoDownloadCount

## Open Items (§12 — deferred)
- [ ] Per-feed HTTP Basic Auth credential storage (encrypted via `IEncryptionManager`)
- [ ] WebSub / PubSubHubbub support for push-based feed updates
- [ ] Per-user pinning (move `is_pinned` to `user_episode_state`)
- [ ] Compatibility testing against Podverse, Kasts, and other gpodder clients
