using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace Hostingaffe.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class TheFile : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "ck_history_subject",
                table: "history");

            migrationBuilder.CreateTable(
                name: "file",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    machine_id = table.Column<Guid>(type: "uuid", nullable: true),
                    installation_id = table.Column<Guid>(type: "uuid", nullable: true),
                    path = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false),
                    created_by = table.Column<Guid>(type: "uuid", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    deleted_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    deleted_by = table.Column<Guid>(type: "uuid", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_file", x => x.id);
                    table.CheckConstraint("ck_file_owner", "num_nonnulls(machine_id, installation_id) = 1");
                    table.ForeignKey(
                        name: "fk_file_created_by",
                        column: x => x.created_by,
                        principalTable: "identity",
                        principalColumn: "id");
                    table.ForeignKey(
                        name: "fk_file_deleted_by",
                        column: x => x.deleted_by,
                        principalTable: "identity",
                        principalColumn: "id");
                    table.ForeignKey(
                        name: "fk_file_installation",
                        column: x => x.installation_id,
                        principalTable: "installation",
                        principalColumn: "id");
                    table.ForeignKey(
                        name: "fk_file_machine",
                        column: x => x.machine_id,
                        principalTable: "machine",
                        principalColumn: "id");
                });

            migrationBuilder.CreateTable(
                name: "file_revision",
                columns: table => new
                {
                    revision = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    file_id = table.Column<Guid>(type: "uuid", nullable: false),
                    content = table.Column<string>(type: "text", nullable: false),
                    executable = table.Column<bool>(type: "boolean", nullable: false),
                    by = table.Column<Guid>(type: "uuid", nullable: false),
                    at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_file_revision", x => new { x.file_id, x.revision });
                    table.ForeignKey(
                        name: "fk_file_revision_by",
                        column: x => x.by,
                        principalTable: "identity",
                        principalColumn: "id");
                    table.ForeignKey(
                        name: "fk_file_revision_file",
                        column: x => x.file_id,
                        principalTable: "file",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.AddCheckConstraint(
                name: "ck_history_subject",
                table: "history",
                sql: "subject in ('page', 'machine', 'software', 'installation', 'file')");

            migrationBuilder.CreateIndex(
                name: "file_on_installation",
                table: "file",
                columns: new[] { "installation_id", "path" },
                unique: true,
                filter: "installation_id is not null");

            migrationBuilder.CreateIndex(
                name: "file_on_machine",
                table: "file",
                columns: new[] { "machine_id", "path" },
                unique: true,
                filter: "machine_id is not null");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "file_revision");

            migrationBuilder.DropTable(
                name: "file");

            migrationBuilder.DropCheckConstraint(
                name: "ck_history_subject",
                table: "history");

            migrationBuilder.AddCheckConstraint(
                name: "ck_history_subject",
                table: "history",
                sql: "subject in ('page', 'machine', 'software', 'installation')");
        }
    }
}
