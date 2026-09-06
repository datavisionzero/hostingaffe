using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Hostingaffe.Infrastructure.Persistence.Migrations
{
    /// <summary>
    /// The first record of the product itself, and the history opened up to
    /// carry more than one kind of subject.
    /// </summary>
    /// <remarks>
    /// The history's <c>page_id</c> becomes a <c>subject</c> and a
    /// <c>subject_id</c>, and the foreign key to the page goes with it: the
    /// pair points at more than one table now, and a row is meant to outlive
    /// what it describes (VISION 7). Every row that exists is a page's, because
    /// a page was the only subject there was.
    /// </remarks>
    public partial class TheMachine : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "fk_history_page",
                table: "history");

            migrationBuilder.DropIndex(
                name: "history_page",
                table: "history");

            migrationBuilder.RenameColumn(
                name: "page_id",
                table: "history",
                newName: "subject_id");

            // Added empty, filled in, then closed: every row that exists is a
            // page's, and a default left on the column would quietly make the
            // next subject a page too.
            migrationBuilder.AddColumn<string>(
                name: "subject",
                table: "history",
                type: "text",
                nullable: true);

            migrationBuilder.Sql("update history set subject = 'page'");

            migrationBuilder.AlterColumn<string>(
                name: "subject",
                table: "history",
                type: "text",
                nullable: false,
                oldClrType: typeof(string),
                oldType: "text",
                oldNullable: true);

            migrationBuilder.CreateTable(
                name: "machine",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    key = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    hostname = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    kind = table.Column<string>(type: "text", nullable: false),
                    host_id = table.Column<Guid>(type: "uuid", nullable: true),
                    provider = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    plan = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    location = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    os = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    arch = table.Column<string>(type: "text", nullable: true),
                    cpu = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    memory = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    disk = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    ipv4 = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    ipv6 = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    private_ip = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    ssh = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    status = table.Column<string>(type: "text", nullable: false),
                    measured_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
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
                    table.PrimaryKey("pk_machine", x => x.id);
                    table.CheckConstraint("ck_machine_arch", "arch is null or arch in ('amd64', 'arm64')");
                    table.CheckConstraint("ck_machine_host", "host_id is null and kind <> 'vm' or kind = 'vm'");
                    table.CheckConstraint("ck_machine_kind", "kind in ('vps', 'dedicated', 'vm', 'local')");
                    table.CheckConstraint("ck_machine_not_its_own_host", "host_id is null or host_id <> id");
                    table.CheckConstraint("ck_machine_status", "status in ('planned', 'active', 'retired')");
                    table.ForeignKey(
                        name: "fk_machine_created_by",
                        column: x => x.created_by,
                        principalTable: "identity",
                        principalColumn: "id");
                    table.ForeignKey(
                        name: "fk_machine_deleted_by",
                        column: x => x.deleted_by,
                        principalTable: "identity",
                        principalColumn: "id");
                    table.ForeignKey(
                        name: "fk_machine_host",
                        column: x => x.host_id,
                        principalTable: "machine",
                        principalColumn: "id");
                    table.ForeignKey(
                        name: "fk_machine_updated_by",
                        column: x => x.updated_by,
                        principalTable: "identity",
                        principalColumn: "id");
                });

            migrationBuilder.CreateIndex(
                name: "history_subject",
                table: "history",
                columns: new[] { "subject", "subject_id", "id" });

            migrationBuilder.AddCheckConstraint(
                name: "ck_history_subject",
                table: "history",
                sql: "subject in ('page', 'machine')");

            migrationBuilder.CreateIndex(
                name: "machine_host",
                table: "machine",
                column: "host_id");

            migrationBuilder.CreateIndex(
                name: "machine_key",
                table: "machine",
                column: "key",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "machine");

            migrationBuilder.DropIndex(
                name: "history_subject",
                table: "history");

            migrationBuilder.DropCheckConstraint(
                name: "ck_history_subject",
                table: "history");

            migrationBuilder.DropColumn(
                name: "subject",
                table: "history");

            migrationBuilder.RenameColumn(
                name: "subject_id",
                table: "history",
                newName: "page_id");

            migrationBuilder.CreateIndex(
                name: "history_page",
                table: "history",
                columns: new[] { "page_id", "id" });

            migrationBuilder.AddForeignKey(
                name: "fk_history_page",
                table: "history",
                column: "page_id",
                principalTable: "page",
                principalColumn: "id",
                onDelete: ReferentialAction.Cascade);
        }
    }
}
