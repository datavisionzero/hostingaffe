using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Hostingaffe.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class TheReportAndTheMachineToken : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "machine_report",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    machine_id = table.Column<Guid>(type: "uuid", nullable: false),
                    number = table.Column<int>(type: "integer", nullable: false),
                    collected_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    received_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    agent = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    body = table.Column<string>(type: "jsonb", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_machine_report", x => x.id);
                    table.CheckConstraint("ck_machine_report_number", "number >= 1");
                    table.ForeignKey(
                        name: "fk_machine_report_machine",
                        column: x => x.machine_id,
                        principalTable: "machine",
                        principalColumn: "id");
                });

            migrationBuilder.CreateTable(
                name: "machine_token",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    machine_id = table.Column<Guid>(type: "uuid", nullable: false),
                    prefix = table.Column<string>(type: "text", nullable: false),
                    secret_hash = table.Column<byte[]>(type: "bytea", nullable: false),
                    issued_by = table.Column<Guid>(type: "uuid", nullable: false),
                    issued_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    last_used_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    revoked_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    revoked_by = table.Column<Guid>(type: "uuid", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_machine_token", x => x.id);
                    table.ForeignKey(
                        name: "fk_machine_token_issued_by",
                        column: x => x.issued_by,
                        principalTable: "identity",
                        principalColumn: "id");
                    table.ForeignKey(
                        name: "fk_machine_token_machine",
                        column: x => x.machine_id,
                        principalTable: "machine",
                        principalColumn: "id");
                    table.ForeignKey(
                        name: "fk_machine_token_revoked_by",
                        column: x => x.revoked_by,
                        principalTable: "identity",
                        principalColumn: "id");
                });

            migrationBuilder.CreateIndex(
                name: "machine_report_number",
                table: "machine_report",
                columns: new[] { "machine_id", "number" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "machine_report_when",
                table: "machine_report",
                columns: new[] { "machine_id", "received_at" },
                descending: new[] { false, true });

            migrationBuilder.CreateIndex(
                name: "machine_token_machine",
                table: "machine_token",
                column: "machine_id",
                unique: true,
                filter: "revoked_at is null");

            migrationBuilder.CreateIndex(
                name: "machine_token_secret_hash",
                table: "machine_token",
                column: "secret_hash",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "machine_report");

            migrationBuilder.DropTable(
                name: "machine_token");
        }
    }
}
