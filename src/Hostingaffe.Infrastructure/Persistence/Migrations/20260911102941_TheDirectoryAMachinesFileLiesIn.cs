using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Hostingaffe.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class TheDirectoryAMachinesFileLiesIn : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "directory",
                table: "file",
                type: "character varying(500)",
                maxLength: 500,
                nullable: true);

            migrationBuilder.AddCheckConstraint(
                name: "ck_file_directory",
                table: "file",
                sql: "directory is null or machine_id is not null");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "ck_file_directory",
                table: "file");

            migrationBuilder.DropColumn(
                name: "directory",
                table: "file");
        }
    }
}
