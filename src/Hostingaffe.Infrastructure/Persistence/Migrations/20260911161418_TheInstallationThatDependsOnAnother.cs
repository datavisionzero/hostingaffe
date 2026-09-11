using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Hostingaffe.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class TheInstallationThatDependsOnAnother : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "installation_depends_on",
                columns: table => new
                {
                    depends_on_id = table.Column<Guid>(type: "uuid", nullable: false),
                    installation_id = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_installation_depends_on", x => new { x.installation_id, x.depends_on_id });
                    table.ForeignKey(
                        name: "fk_installation_depends_on_installation",
                        column: x => x.installation_id,
                        principalTable: "installation",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "fk_installation_depends_on_target",
                        column: x => x.depends_on_id,
                        principalTable: "installation",
                        principalColumn: "id");
                });

            migrationBuilder.CreateIndex(
                name: "installation_depends_on_target",
                table: "installation_depends_on",
                column: "depends_on_id");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "installation_depends_on");
        }
    }
}
