using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Jellyfin.Plugin.Template.Data.Migrations;

/// <inheritdoc />
public partial class ApiSupport : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.AddColumn<bool>(
            name: "IsSubscribed",
            table: "Subscriptions",
            type: "INTEGER",
            nullable: false,
            defaultValue: true);

        migrationBuilder.AddColumn<DateTime>(
            name: "SubscriptionChanged",
            table: "Subscriptions",
            type: "TEXT",
            nullable: true);

        migrationBuilder.AddColumn<DateTime>(
            name: "GuidChanged",
            table: "Subscriptions",
            type: "TEXT",
            nullable: true);

        migrationBuilder.AddColumn<string>(
            name: "NewGuid",
            table: "Subscriptions",
            type: "TEXT",
            nullable: true);

        migrationBuilder.AddColumn<DateTime>(
            name: "Deleted",
            table: "Subscriptions",
            type: "TEXT",
            nullable: true);

        migrationBuilder.CreateTable(
            name: "AppPasswords",
            columns: table => new
            {
                Id = table.Column<Guid>(type: "TEXT", nullable: false),
                UserId = table.Column<Guid>(type: "TEXT", nullable: false),
                Label = table.Column<string>(type: "TEXT", nullable: false),
                Kind = table.Column<string>(type: "TEXT", nullable: false, defaultValue: "openpodcastapi"),
                TokenHash = table.Column<string>(type: "TEXT", nullable: false),
                CreatedAt = table.Column<DateTime>(type: "TEXT", nullable: false),
                LastUsedAt = table.Column<DateTime>(type: "TEXT", nullable: true),
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_AppPasswords", x => x.Id);
            });

        migrationBuilder.CreateTable(
            name: "DeletionRequests",
            columns: table => new
            {
                Id = table.Column<int>(type: "INTEGER", nullable: false)
                    .Annotation("Sqlite:Autoincrement", true),
                UserId = table.Column<Guid>(type: "TEXT", nullable: false),
                FeedId = table.Column<string>(type: "TEXT", nullable: false),
                Status = table.Column<string>(type: "TEXT", nullable: false, defaultValue: "PENDING"),
                Message = table.Column<string>(type: "TEXT", nullable: true),
                RequestedAt = table.Column<DateTime>(type: "TEXT", nullable: false),
                CompletedAt = table.Column<DateTime>(type: "TEXT", nullable: true),
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_DeletionRequests", x => x.Id);
            });
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropTable(name: "DeletionRequests");
        migrationBuilder.DropTable(name: "AppPasswords");

        migrationBuilder.DropColumn(name: "IsSubscribed", table: "Subscriptions");
        migrationBuilder.DropColumn(name: "SubscriptionChanged", table: "Subscriptions");
        migrationBuilder.DropColumn(name: "GuidChanged", table: "Subscriptions");
        migrationBuilder.DropColumn(name: "NewGuid", table: "Subscriptions");
        migrationBuilder.DropColumn(name: "Deleted", table: "Subscriptions");
    }
}
