using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Jellyfin.Plugin.Podcasts.Database.Migrations;

/// <inheritdoc />
public partial class InitialCreate : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.CreateTable(
            name: "podcasts",
            columns: table => new
            {
                id = table.Column<Guid>(type: "TEXT", nullable: false),
                feed_url = table.Column<string>(type: "TEXT", nullable: false),
                guid = table.Column<string>(type: "TEXT", nullable: true),
                title = table.Column<string>(type: "TEXT", nullable: true),
                description = table.Column<string>(type: "TEXT", nullable: true),
                image_url = table.Column<string>(type: "TEXT", nullable: true),
                author = table.Column<string>(type: "TEXT", nullable: true),
                language = table.Column<string>(type: "TEXT", nullable: true),
                last_fetched_at = table.Column<DateTime>(type: "TEXT", nullable: true),
                fetch_error = table.Column<string>(type: "TEXT", nullable: true),
                created_at = table.Column<DateTime>(type: "TEXT", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_podcasts", x => x.id);
            });

        migrationBuilder.CreateTable(
            name: "app_passwords",
            columns: table => new
            {
                id = table.Column<Guid>(type: "TEXT", nullable: false),
                user_id = table.Column<string>(type: "TEXT", nullable: false),
                label = table.Column<string>(type: "TEXT", nullable: false),
                password_hash = table.Column<string>(type: "TEXT", nullable: false),
                created_at = table.Column<DateTime>(type: "TEXT", nullable: false),
                last_used_at = table.Column<DateTime>(type: "TEXT", nullable: true)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_app_passwords", x => x.id);
            });

        migrationBuilder.CreateTable(
            name: "episodes",
            columns: table => new
            {
                id = table.Column<Guid>(type: "TEXT", nullable: false),
                podcast_id = table.Column<Guid>(type: "TEXT", nullable: false),
                guid = table.Column<string>(type: "TEXT", nullable: false),
                title = table.Column<string>(type: "TEXT", nullable: true),
                description = table.Column<string>(type: "TEXT", nullable: true),
                published_at = table.Column<DateTime>(type: "TEXT", nullable: true),
                duration_seconds = table.Column<int>(type: "INTEGER", nullable: true),
                enclosure_url = table.Column<string>(type: "TEXT", nullable: false),
                enclosure_mime = table.Column<string>(type: "TEXT", nullable: true),
                enclosure_length = table.Column<long>(type: "INTEGER", nullable: true),
                image_url = table.Column<string>(type: "TEXT", nullable: true),
                is_cached = table.Column<bool>(type: "INTEGER", nullable: false),
                cached_at = table.Column<DateTime>(type: "TEXT", nullable: true),
                cached_size_bytes = table.Column<long>(type: "INTEGER", nullable: true),
                is_pinned = table.Column<bool>(type: "INTEGER", nullable: false, defaultValue: false),
                local_path = table.Column<string>(type: "TEXT", nullable: true),
                jellyfin_item_id = table.Column<string>(type: "TEXT", nullable: true),
                created_at = table.Column<DateTime>(type: "TEXT", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_episodes", x => x.id);
                table.ForeignKey(
                    name: "FK_episodes_podcasts_podcast_id",
                    column: x => x.podcast_id,
                    principalTable: "podcasts",
                    principalColumn: "id",
                    onDelete: ReferentialAction.Cascade);
            });

        migrationBuilder.CreateTable(
            name: "user_subscriptions",
            columns: table => new
            {
                id = table.Column<Guid>(type: "TEXT", nullable: false),
                user_id = table.Column<string>(type: "TEXT", nullable: false),
                podcast_id = table.Column<Guid>(type: "TEXT", nullable: false),
                is_subscribed = table.Column<bool>(type: "INTEGER", nullable: false, defaultValue: true),
                subscription_changed_at = table.Column<DateTime>(type: "TEXT", nullable: false),
                deleted_at = table.Column<DateTime>(type: "TEXT", nullable: true)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_user_subscriptions", x => x.id);
                table.ForeignKey(
                    name: "FK_user_subscriptions_podcasts_podcast_id",
                    column: x => x.podcast_id,
                    principalTable: "podcasts",
                    principalColumn: "id",
                    onDelete: ReferentialAction.Cascade);
            });

        migrationBuilder.CreateTable(
            name: "user_episode_state",
            columns: table => new
            {
                id = table.Column<Guid>(type: "TEXT", nullable: false),
                user_id = table.Column<string>(type: "TEXT", nullable: false),
                episode_id = table.Column<Guid>(type: "TEXT", nullable: false),
                position_seconds = table.Column<int>(type: "INTEGER", nullable: false),
                is_played = table.Column<bool>(type: "INTEGER", nullable: false),
                updated_at = table.Column<DateTime>(type: "TEXT", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_user_episode_state", x => x.id);
                table.ForeignKey(
                    name: "FK_user_episode_state_episodes_episode_id",
                    column: x => x.episode_id,
                    principalTable: "episodes",
                    principalColumn: "id",
                    onDelete: ReferentialAction.Cascade);
            });

        // Unique indexes
        migrationBuilder.CreateIndex(
            name: "IX_podcasts_feed_url",
            table: "podcasts",
            column: "feed_url",
            unique: true);

        migrationBuilder.CreateIndex(
            name: "IX_episodes_podcast_id_guid",
            table: "episodes",
            columns: new[] { "podcast_id", "guid" },
            unique: true);

        migrationBuilder.CreateIndex(
            name: "IX_user_subscriptions_user_id_podcast_id",
            table: "user_subscriptions",
            columns: new[] { "user_id", "podcast_id" },
            unique: true);

        migrationBuilder.CreateIndex(
            name: "IX_user_episode_state_user_id_episode_id",
            table: "user_episode_state",
            columns: new[] { "user_id", "episode_id" },
            unique: true);
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropTable(name: "user_episode_state");
        migrationBuilder.DropTable(name: "user_subscriptions");
        migrationBuilder.DropTable(name: "episodes");
        migrationBuilder.DropTable(name: "app_passwords");
        migrationBuilder.DropTable(name: "podcasts");
    }
}
