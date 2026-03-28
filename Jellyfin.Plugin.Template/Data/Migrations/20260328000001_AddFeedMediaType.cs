using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Jellyfin.Plugin.Template.Data.Migrations;

/// <inheritdoc />
public partial class AddFeedMediaType : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.AddColumn<string>(
            name: "MediaType",
            table: "Feeds",
            type: "TEXT",
            nullable: false,
            defaultValue: "Audio");
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropColumn(
            name: "MediaType",
            table: "Feeds");
    }
}
