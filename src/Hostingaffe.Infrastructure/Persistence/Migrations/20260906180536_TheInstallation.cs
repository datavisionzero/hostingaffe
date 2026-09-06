using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Hostingaffe.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class TheInstallation : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "ck_history_subject",
                table: "history");

            migrationBuilder.CreateTable(
                name: "installation",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    key = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    machine_id = table.Column<Guid>(type: "uuid", nullable: false),
                    software_id = table.Column<Guid>(type: "uuid", nullable: false),
                    environment = table.Column<string>(type: "text", nullable: false),
                    role = table.Column<string>(type: "text", nullable: false),
                    status = table.Column<string>(type: "text", nullable: false),
                    urls = table.Column<string[]>(type: "text[]", nullable: false, defaultValue: new string[0]),
                    path = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    secrets = table.Column<string[]>(type: "text[]", nullable: false, defaultValue: new string[0]),
                    backup = table.Column<string>(type: "text", nullable: false),
                    monitoring = table.Column<string>(type: "text", nullable: false),
                    logging = table.Column<string>(type: "text", nullable: false),
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
                    table.PrimaryKey("pk_installation", x => x.id);
                    table.CheckConstraint("ck_installation_backup", "backup in ('none', 'planned', 'active')");
                    table.CheckConstraint("ck_installation_environment", "environment in ('production', 'staging', 'development')");
                    table.CheckConstraint("ck_installation_logging", "logging in ('local', 'central')");
                    table.CheckConstraint("ck_installation_monitoring", "monitoring in ('none', 'external')");
                    table.CheckConstraint("ck_installation_role", "role in ('application', 'platform')");
                    table.CheckConstraint("ck_installation_status", "status in ('planned', 'active', 'retired')");
                    table.ForeignKey(
                        name: "fk_installation_created_by",
                        column: x => x.created_by,
                        principalTable: "identity",
                        principalColumn: "id");
                    table.ForeignKey(
                        name: "fk_installation_deleted_by",
                        column: x => x.deleted_by,
                        principalTable: "identity",
                        principalColumn: "id");
                    table.ForeignKey(
                        name: "fk_installation_machine",
                        column: x => x.machine_id,
                        principalTable: "machine",
                        principalColumn: "id");
                    table.ForeignKey(
                        name: "fk_installation_software",
                        column: x => x.software_id,
                        principalTable: "software",
                        principalColumn: "id");
                    table.ForeignKey(
                        name: "fk_installation_updated_by",
                        column: x => x.updated_by,
                        principalTable: "identity",
                        principalColumn: "id");
                });

            migrationBuilder.CreateTable(
                name: "installation_port",
                columns: table => new
                {
                    port = table.Column<int>(type: "integer", nullable: false),
                    protocol = table.Column<string>(type: "text", nullable: false),
                    installation_id = table.Column<Guid>(type: "uuid", nullable: false),
                    scope = table.Column<string>(type: "text", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_installation_port", x => new { x.installation_id, x.port, x.protocol });
                    table.CheckConstraint("ck_installation_port_number", "port between 1 and 65535");
                    table.CheckConstraint("ck_installation_port_protocol", "protocol in ('tcp', 'udp')");
                    table.CheckConstraint("ck_installation_port_scope", "scope in ('public', 'private', 'internal')");
                    table.ForeignKey(
                        name: "fk_installation_port_installation",
                        column: x => x.installation_id,
                        principalTable: "installation",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.AddCheckConstraint(
                name: "ck_history_subject",
                table: "history",
                sql: "subject in ('page', 'machine', 'software', 'installation')");

            migrationBuilder.CreateIndex(
                name: "installation_key",
                table: "installation",
                column: "key",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "installation_machine",
                table: "installation",
                column: "machine_id");

            migrationBuilder.CreateIndex(
                name: "installation_software",
                table: "installation",
                column: "software_id");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "installation_port");

            migrationBuilder.DropTable(
                name: "installation");

            migrationBuilder.DropCheckConstraint(
                name: "ck_history_subject",
                table: "history");

            migrationBuilder.AddCheckConstraint(
                name: "ck_history_subject",
                table: "history",
                sql: "subject in ('page', 'machine', 'software')");
        }
    }
}
