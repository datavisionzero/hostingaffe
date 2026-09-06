using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Hostingaffe.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class ThePageGetsKindAndAttachedTo : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "installation_id",
                table: "page",
                type: "uuid",
                nullable: true);

            // Added empty and filled in one go rather than with a column
            // default: `runbook` is the first value of the enum and so the CLR
            // default, and a column default would make EF leave the column out
            // of every insert that meant `runbook`. Pages written before this
            // column existed become `note`, which is the kind that claims
            // nothing.
            migrationBuilder.AddColumn<string>(
                name: "kind",
                table: "page",
                type: "text",
                nullable: true);

            migrationBuilder.Sql("update page set kind = 'note' where kind is null");

            migrationBuilder.AlterColumn<string>(
                name: "kind",
                table: "page",
                type: "text",
                nullable: false);

            migrationBuilder.AddColumn<Guid>(
                name: "machine_id",
                table: "page",
                type: "uuid",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "page_on_installation",
                table: "page",
                column: "installation_id",
                filter: "installation_id is not null");

            migrationBuilder.CreateIndex(
                name: "page_on_machine",
                table: "page",
                column: "machine_id",
                filter: "machine_id is not null");

            migrationBuilder.AddCheckConstraint(
                name: "ck_page_attached_to",
                table: "page",
                sql: "num_nonnulls(machine_id, installation_id) <= 1");

            migrationBuilder.AddCheckConstraint(
                name: "ck_page_kind",
                table: "page",
                sql: "kind in ('runbook', 'decision', 'note')");

            migrationBuilder.AddForeignKey(
                name: "fk_page_installation",
                table: "page",
                column: "installation_id",
                principalTable: "installation",
                principalColumn: "id");

            migrationBuilder.AddForeignKey(
                name: "fk_page_machine",
                table: "page",
                column: "machine_id",
                principalTable: "machine",
                principalColumn: "id");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "fk_page_installation",
                table: "page");

            migrationBuilder.DropForeignKey(
                name: "fk_page_machine",
                table: "page");

            migrationBuilder.DropIndex(
                name: "page_on_installation",
                table: "page");

            migrationBuilder.DropIndex(
                name: "page_on_machine",
                table: "page");

            migrationBuilder.DropCheckConstraint(
                name: "ck_page_attached_to",
                table: "page");

            migrationBuilder.DropCheckConstraint(
                name: "ck_page_kind",
                table: "page");

            migrationBuilder.DropColumn(
                name: "installation_id",
                table: "page");

            migrationBuilder.DropColumn(
                name: "kind",
                table: "page");

            migrationBuilder.DropColumn(
                name: "machine_id",
                table: "page");
        }
    }
}
