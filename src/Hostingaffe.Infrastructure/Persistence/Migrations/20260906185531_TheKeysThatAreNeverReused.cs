using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Hostingaffe.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class TheKeysThatAreNeverReused : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "assigned_key",
                columns: table => new
                {
                    kind = table.Column<string>(type: "text", nullable: false),
                    key = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_assigned_key", x => new { x.kind, x.key });
                    table.CheckConstraint("ck_assigned_key_kind", "kind in ('machine', 'software', 'installation')");
                });
            // Whatever is already there had its key given out before this table
            // existed, and the rule is not "from now on".
            migrationBuilder.Sql("insert into assigned_key (kind, key) select 'machine', key from machine on conflict do nothing");
            migrationBuilder.Sql("insert into assigned_key (kind, key) select 'software', key from software on conflict do nothing");
            migrationBuilder.Sql("insert into assigned_key (kind, key) select 'installation', key from installation on conflict do nothing");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "assigned_key");
        }
    }
}
