using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Hostingaffe.Infrastructure.Persistence.Migrations
{
    /// <summary>
    /// Monitoring gets the third value backup already has: a monitor that was
    /// decided on and deferred is <c>planned</c>, not <c>none</c>, so that
    /// "every production installation nobody watches" stops naming the ones
    /// somebody already thought about. Nothing is rewritten — <c>none</c> stays
    /// <c>none</c>; only the constraint widens.
    /// </summary>
    /// <inheritdoc />
    public partial class TheMonitoringGetsPlanned : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "ck_installation_monitoring",
                table: "installation");

            migrationBuilder.AddCheckConstraint(
                name: "ck_installation_monitoring",
                table: "installation",
                sql: "monitoring in ('none', 'planned', 'external')");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "ck_installation_monitoring",
                table: "installation");

            migrationBuilder.AddCheckConstraint(
                name: "ck_installation_monitoring",
                table: "installation",
                sql: "monitoring in ('none', 'external')");
        }
    }
}
