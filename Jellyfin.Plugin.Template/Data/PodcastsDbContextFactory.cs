using System.IO;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace Jellyfin.Plugin.Template.Data;

/// <summary>
/// Design-time factory used by <c>dotnet ef migrations</c> tooling.
/// Not used at runtime.
/// </summary>
public class PodcastsDbContextFactory : IDesignTimeDbContextFactory<PodcastsDbContext>
{
    /// <inheritdoc />
    public PodcastsDbContext CreateDbContext(string[] args)
    {
        var dbPath = Path.Combine(Path.GetTempPath(), "podcasts-designtime.db");
        var options = new DbContextOptionsBuilder<PodcastsDbContext>()
            .UseSqlite($"Data Source={dbPath}")
            .Options;
        return new PodcastsDbContext(options);
    }
}
