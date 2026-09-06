using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Hostingaffe.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddAgentMetadata : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "metadata",
                table: "identity",
                type: "jsonb",
                nullable: true);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "metadata_reported_at",
                table: "identity",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "identity_metadata",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    identity_id = table.Column<Guid>(type: "uuid", nullable: false),
                    reported_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    metadata = table.Column<string>(type: "jsonb", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_identity_metadata", x => x.id);
                    table.ForeignKey(
                        name: "fk_identity_metadata_identity",
                        column: x => x.identity_id,
                        principalTable: "identity",
                        principalColumn: "id");
                });

            migrationBuilder.CreateIndex(
                name: "identity_metadata_identity",
                table: "identity_metadata",
                columns: new[] { "identity_id", "reported_at" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "identity_metadata");

            migrationBuilder.DropColumn(
                name: "metadata",
                table: "identity");

            migrationBuilder.DropColumn(
                name: "metadata_reported_at",
                table: "identity");
        }
    }
}
