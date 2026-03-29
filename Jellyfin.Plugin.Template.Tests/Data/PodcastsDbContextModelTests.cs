using Jellyfin.Plugin.Template.Data;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace Jellyfin.Plugin.Template.Tests.Data;

public class PodcastsDbContextModelTests
{
    [Fact]
    public void DatabaseModel_HasNoPendingMigrationChanges()
    {
        using var connection = new SqliteConnection("Data Source=:memory:");
        connection.Open();

        var options = new DbContextOptionsBuilder<PodcastsDbContext>()
            .UseSqlite(connection)
            .Options;

        using var context = new PodcastsDbContext(options);
        Assert.False(context.Database.HasPendingModelChanges());
    }
}
