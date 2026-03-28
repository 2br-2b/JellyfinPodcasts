using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Jellyfin.Plugin.Template.Data.Migrations;

/// <inheritdoc />
public partial class InitialCreate : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.CreateTable(
            name: "Feeds",
            columns: table => new
            {
                Id = table.Column<string>(type: "TEXT", nullable: false),
                FeedUrl = table.Column<string>(type: "TEXT", nullable: false),
                Title = table.Column<string>(type: "TEXT", nullable: false),
                Description = table.Column<string>(type: "TEXT", nullable: false),
                ImageUrl = table.Column<string>(type: "TEXT", nullable: true),
                HomePageUrl = table.Column<string>(type: "TEXT", nullable: true),
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_Feeds", x => x.Id);
            });

        migrationBuilder.CreateTable(
            name: "Subscriptions",
            columns: table => new
            {
                UserId = table.Column<Guid>(type: "TEXT", nullable: false),
                FeedId = table.Column<string>(type: "TEXT", nullable: false),
                DateAdded = table.Column<DateTime>(type: "TEXT", nullable: false),
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_Subscriptions", x => new { x.UserId, x.FeedId });
                table.ForeignKey(
                    name: "FK_Subscriptions_Feeds_FeedId",
                    column: x => x.FeedId,
                    principalTable: "Feeds",
                    principalColumn: "Id",
                    onDelete: ReferentialAction.Cascade);
            });

        migrationBuilder.CreateIndex(
            name: "IX_Subscriptions_FeedId",
            table: "Subscriptions",
            column: "FeedId");
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropTable(name: "Subscriptions");
        migrationBuilder.DropTable(name: "Feeds");
    }
}
