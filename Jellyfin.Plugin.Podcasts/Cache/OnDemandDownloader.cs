using Jellyfin.Plugin.Podcasts.Database;
using Microsoft.Extensions.Logging;

namespace Jellyfin.Plugin.Podcasts.Cache;

/// <summary>
/// Streams an uncached episode from its origin URL to the client while simultaneously
/// writing it to the local disk cache (tee-pipe pattern).
/// </summary>
public class OnDemandDownloader
{
    private readonly PodcastDbContext _db;
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly CacheManager _cacheManager;
    private readonly ILogger<OnDemandDownloader> _logger;

    public OnDemandDownloader(
        PodcastDbContext db,
        IHttpClientFactory httpClientFactory,
        CacheManager cacheManager,
        ILogger<OnDemandDownloader> logger)
    {
        _db = db;
        _httpClientFactory = httpClientFactory;
        _cacheManager = cacheManager;
        _logger = logger;
    }

    /// <summary>
    /// Returns a stream for the given episode. If cached, opens the local file.
    /// If uncached, downloads while simultaneously writing to cache (tee-pipe).
    /// After a full download, triggers background eviction.
    /// </summary>
    public async Task<(Stream stream, string? contentType, long? contentLength)> GetStreamAsync(
        Guid episodeId, CancellationToken ct)
    {
        var episode = await _db.Episodes.FindAsync(new object[] { episodeId }, ct).ConfigureAwait(false);
        if (episode is null)
            throw new FileNotFoundException($"Episode {episodeId} not found");

        // Check cache (may have been downloaded since the initial library request)
        if (episode.IsCached && episode.LocalPath is not null)
        {
            var cachedPath = _cacheManager.GetCachedPath(episode.LocalPath);
            if (cachedPath is not null)
            {
                // Refresh cached_at to update LRU order
                episode.CachedAt = DateTime.UtcNow;
                await _db.SaveChangesAsync(ct).ConfigureAwait(false);

                var fileStream = File.OpenRead(cachedPath);
                return (fileStream, episode.EnclosureMime, new FileInfo(cachedPath).Length);
            }
        }

        // Not cached: tee-pipe — download and stream simultaneously
        _logger.LogInformation("On-demand streaming episode {EpisodeId} from {Url}", episodeId, episode.EnclosureUrl);

        var client = _httpClientFactory.CreateClient("PodcastFeedClient");
        var response = await client.GetAsync(episode.EnclosureUrl, HttpCompletionOption.ResponseHeadersRead, ct).ConfigureAwait(false);
        response.EnsureSuccessStatusCode();

        var ext = GetExtension(episode.EnclosureMime, episode.EnclosureUrl);
        var relativePath = Path.Combine(episode.PodcastId.ToString(), $"{episode.Id}{ext}");
        var fullPath = Path.Combine(_cacheManager.CacheRoot, relativePath);
        Directory.CreateDirectory(Path.GetDirectoryName(fullPath)!);

        var remoteStream = await response.Content.ReadAsStreamAsync(ct).ConfigureAwait(false);
        var contentLength = response.Content.Headers.ContentLength;
        var contentType = episode.EnclosureMime ?? response.Content.Headers.ContentType?.MediaType;

        // Tee stream: writes to disk, returns the tee for the caller to read
        var teeStream = new TeeStream(remoteStream, fullPath, async (fileSize) =>
        {
            // Called after full download completes
            try
            {
                episode.IsCached = true;
                episode.CachedAt = DateTime.UtcNow;
                episode.CachedSizeBytes = fileSize;
                episode.LocalPath = relativePath;
                await _db.SaveChangesAsync(CancellationToken.None).ConfigureAwait(false);

                // Background eviction
                _ = _cacheManager.EvictAsync(CancellationToken.None);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to update cache state for episode {EpisodeId}", episodeId);
            }
        });

        return (teeStream, contentType, contentLength);
    }

    private static string GetExtension(string? mime, string url)
    {
        return mime switch
        {
            "audio/mpeg" => ".mp3",
            "audio/mp4" => ".m4a",
            "audio/ogg" => ".ogg",
            "audio/opus" => ".opus",
            "audio/flac" => ".flac",
            "audio/aac" => ".aac",
            "audio/wav" => ".wav",
            _ => Path.GetExtension(new Uri(url).AbsolutePath) is { Length: > 0 } ext ? ext : ".mp3"
        };
    }
}

/// <summary>
/// A stream that reads from a source and simultaneously writes all bytes to a file.
/// On complete read (or disposal), invokes a completion callback with the total bytes written.
/// </summary>
internal sealed class TeeStream : Stream
{
    private readonly Stream _source;
    private readonly FileStream _file;
    private readonly Func<long, Task> _onComplete;
    private long _bytesWritten;
    private bool _completed;

    public TeeStream(Stream source, string filePath, Func<long, Task> onComplete)
    {
        _source = source;
        _file = File.Create(filePath);
        _onComplete = onComplete;
    }

    public override bool CanRead => true;
    public override bool CanSeek => false;
    public override bool CanWrite => false;
    public override long Length => throw new NotSupportedException();
    public override long Position { get => throw new NotSupportedException(); set => throw new NotSupportedException(); }

    public override int Read(byte[] buffer, int offset, int count)
    {
        var read = _source.Read(buffer, offset, count);
        if (read > 0)
        {
            _file.Write(buffer, offset, read);
            _bytesWritten += read;
        }
        else if (!_completed)
        {
            _completed = true;
            _file.Flush();
            _file.Close();
            _ = _onComplete(_bytesWritten);
        }
        return read;
    }

    public override async Task<int> ReadAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken)
    {
        var read = await _source.ReadAsync(buffer, offset, count, cancellationToken).ConfigureAwait(false);
        if (read > 0)
        {
            await _file.WriteAsync(buffer, offset, read, cancellationToken).ConfigureAwait(false);
            _bytesWritten += read;
        }
        else if (!_completed)
        {
            _completed = true;
            await _file.FlushAsync(cancellationToken).ConfigureAwait(false);
            _file.Close();
            await _onComplete(_bytesWritten).ConfigureAwait(false);
        }
        return read;
    }

    public override void Flush() { }
    public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
    public override void SetLength(long value) => throw new NotSupportedException();
    public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            if (!_completed)
            {
                _completed = true;
                try { _file.Flush(); _file.Close(); } catch { /* ignore */ }
                _ = _onComplete(_bytesWritten);
            }
            _source.Dispose();
            _file.Dispose();
        }
        base.Dispose(disposing);
    }
}
