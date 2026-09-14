using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Hostingaffe.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class ThePortThatReachesThisMachineOnly : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "ck_machine_port_scope",
                table: "machine_port");

            migrationBuilder.DropCheckConstraint(
                name: "ck_installation_port_scope",
                table: "installation_port");

            migrationBuilder.AddCheckConstraint(
                name: "ck_machine_port_scope",
                table: "machine_port",
                sql: "scope in ('public', 'private', 'loopback', 'internal')");

            migrationBuilder.AddCheckConstraint(
                name: "ck_installation_port_scope",
                table: "installation_port",
                sql: "scope in ('public', 'private', 'loopback', 'internal')");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "ck_machine_port_scope",
                table: "machine_port");

            migrationBuilder.DropCheckConstraint(
                name: "ck_installation_port_scope",
                table: "installation_port");

            migrationBuilder.AddCheckConstraint(
                name: "ck_machine_port_scope",
                table: "machine_port",
                sql: "scope in ('public', 'private', 'internal')");

            migrationBuilder.AddCheckConstraint(
                name: "ck_installation_port_scope",
                table: "installation_port",
                sql: "scope in ('public', 'private', 'internal')");
        }
    }
}
