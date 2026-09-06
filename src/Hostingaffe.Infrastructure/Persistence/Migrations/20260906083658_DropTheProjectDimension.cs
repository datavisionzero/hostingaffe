using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Hostingaffe.Infrastructure.Persistence.Migrations
{
    /// <summary>
    /// The project and everything that hung on it. One instance holds one
    /// team's infrastructure and every user sees all of it (VISION 9), so the
    /// wiki is instance-wide and its slug is unique instance-wide.
    /// </summary>
    public partial class DropTheProjectDimension : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "fk_page_project",
                table: "page");

            migrationBuilder.DropTable(
                name: "project_access");

            migrationBuilder.DropTable(
                name: "project");

            migrationBuilder.DropIndex(
                name: "page_slug",
                table: "page");

            migrationBuilder.DropColumn(
                name: "project_id",
                table: "page");

            migrationBuilder.CreateIndex(
                name: "page_slug",
                table: "page",
                column: "slug",
                unique: true);
        }

        /// <summary>
        /// Migrations only run forward (ADR 0011); nothing calls this.
        /// </summary>
        protected override void Down(MigrationBuilder migrationBuilder) =>
            throw new NotSupportedException("Migrations only run forward (ADR 0011).");
    }
}
