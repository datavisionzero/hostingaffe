using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Hostingaffe.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class ThePortsOfTheMachineItself : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "machine_port",
                columns: table => new
                {
                    port = table.Column<int>(type: "integer", nullable: false),
                    protocol = table.Column<string>(type: "text", nullable: false),
                    machine_id = table.Column<Guid>(type: "uuid", nullable: false),
                    scope = table.Column<string>(type: "text", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_machine_port", x => new { x.machine_id, x.port, x.protocol });
                    table.CheckConstraint("ck_machine_port_number", "port between 1 and 65535");
                    table.CheckConstraint("ck_machine_port_protocol", "protocol in ('tcp', 'udp')");
                    table.CheckConstraint("ck_machine_port_scope", "scope in ('public', 'private', 'internal')");
                    table.ForeignKey(
                        name: "fk_machine_port_machine",
                        column: x => x.machine_id,
                        principalTable: "machine",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "machine_port");
        }
    }
}
