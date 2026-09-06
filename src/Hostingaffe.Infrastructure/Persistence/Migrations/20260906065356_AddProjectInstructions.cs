using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Hostingaffe.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddProjectInstructions : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "instructions_page_id",
                table: "project",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddForeignKey(
                name: "fk_project_instructions_page",
                table: "project",
                column: "instructions_page_id",
                principalTable: "page",
                principalColumn: "id",
                onDelete: ReferentialAction.SetNull);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "fk_project_instructions_page",
                table: "project");

            migrationBuilder.DropColumn(
                name: "instructions_page_id",
                table: "project");
        }
    }
}
