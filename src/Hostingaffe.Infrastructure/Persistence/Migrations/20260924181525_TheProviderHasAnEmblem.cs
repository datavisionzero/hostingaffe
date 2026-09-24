using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Hostingaffe.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class TheProviderHasAnEmblem : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "emblem",
                table: "provider",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "emblem_palette",
                table: "provider",
                type: "text",
                nullable: true);

            migrationBuilder.AddCheckConstraint(
                name: "ck_provider_emblem",
                table: "provider",
                sql: "emblem is null or emblem in ('orbit', 'arch', 'peak', 'split', 'quarter', 'stack', 'wave', 'grid', 'target', 'bloom', 'eclipse', 'chevron', 'bridge', 'tiles', 'beam', 'steps')");

            migrationBuilder.AddCheckConstraint(
                name: "ck_provider_emblem_palette",
                table: "provider",
                sql: "emblem_palette is null or emblem_palette in ('bauhaus', 'ember', 'meadow', 'lagoon', 'dusk', 'citrus', 'orchid', 'granite', 'coral', 'glacier')");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "ck_provider_emblem",
                table: "provider");

            migrationBuilder.DropCheckConstraint(
                name: "ck_provider_emblem_palette",
                table: "provider");

            migrationBuilder.DropColumn(
                name: "emblem",
                table: "provider");

            migrationBuilder.DropColumn(
                name: "emblem_palette",
                table: "provider");
        }
    }
}
