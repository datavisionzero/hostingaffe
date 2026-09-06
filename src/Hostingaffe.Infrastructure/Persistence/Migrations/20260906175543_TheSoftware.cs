using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Hostingaffe.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class TheSoftware : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "ck_history_subject",
                table: "history");

            migrationBuilder.CreateTable(
                name: "software",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    key = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    homepage = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    repository = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    image = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    description = table.Column<string>(type: "text", nullable: false, defaultValue: ""),
                    created_by = table.Column<Guid>(type: "uuid", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_by = table.Column<Guid>(type: "uuid", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    deleted_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    deleted_by = table.Column<Guid>(type: "uuid", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_software", x => x.id);
                    table.ForeignKey(
                        name: "fk_software_created_by",
                        column: x => x.created_by,
                        principalTable: "identity",
                        principalColumn: "id");
                    table.ForeignKey(
                        name: "fk_software_deleted_by",
                        column: x => x.deleted_by,
                        principalTable: "identity",
                        principalColumn: "id");
                    table.ForeignKey(
                        name: "fk_software_updated_by",
                        column: x => x.updated_by,
                        principalTable: "identity",
                        principalColumn: "id");
                });

            migrationBuilder.AddCheckConstraint(
                name: "ck_history_subject",
                table: "history",
                sql: "subject in ('page', 'machine', 'software')");

            migrationBuilder.CreateIndex(
                name: "software_key",
                table: "software",
                column: "key",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "software");

            migrationBuilder.DropCheckConstraint(
                name: "ck_history_subject",
                table: "history");

            migrationBuilder.AddCheckConstraint(
                name: "ck_history_subject",
                table: "history",
                sql: "subject in ('page', 'machine')");
        }
    }
}
