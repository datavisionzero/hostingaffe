using System;
using Microsoft.EntityFrameworkCore.Migrations;
using NpgsqlTypes;

#nullable disable

namespace Hostingaffe.Infrastructure.Persistence.Migrations
{
    /// <summary>
    /// planaffe's ticket model, dropped: issue, epic, release, label, claim,
    /// comment, question and the wake channel's subjects. Nothing has been
    /// released, so no instance carries data in these tables.
    /// </summary>
    public partial class DropTheIssueComplex : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // `issue_read` is raw SQL in an earlier migration, so EF does not
            // know it stands on `issue` and would let the drop below fail.
            migrationBuilder.Sql("drop view issue_read;");

            migrationBuilder.DropForeignKey(
                name: "fk_history_epic",
                table: "history");

            migrationBuilder.DropForeignKey(
                name: "fk_history_issue",
                table: "history");

            migrationBuilder.DropTable(
                name: "blocker");

            migrationBuilder.DropTable(
                name: "comment");

            migrationBuilder.DropTable(
                name: "epic_label");

            migrationBuilder.DropTable(
                name: "issue_label");

            migrationBuilder.DropTable(
                name: "page_label");

            migrationBuilder.DropTable(
                name: "question");

            migrationBuilder.DropTable(
                name: "release_issue");

            migrationBuilder.DropTable(
                name: "label");

            migrationBuilder.DropTable(
                name: "issue");

            migrationBuilder.DropTable(
                name: "release");

            migrationBuilder.DropTable(
                name: "epic");

            migrationBuilder.DropIndex(
                name: "history_epic",
                table: "history");

            migrationBuilder.DropIndex(
                name: "history_issue",
                table: "history");

            migrationBuilder.DropCheckConstraint(
                name: "ck_history_subject",
                table: "history");

            migrationBuilder.DropColumn(
                name: "last_epic_number",
                table: "project");

            migrationBuilder.DropColumn(
                name: "last_issue_number",
                table: "project");

            migrationBuilder.DropColumn(
                name: "review_required",
                table: "project");

            migrationBuilder.DropColumn(
                name: "triage_required",
                table: "project");

            migrationBuilder.DropColumn(
                name: "epic_id",
                table: "history");

            migrationBuilder.DropColumn(
                name: "issue_id",
                table: "history");

            migrationBuilder.AlterColumn<Guid>(
                name: "page_id",
                table: "history",
                type: "uuid",
                nullable: false,
                defaultValue: new Guid("00000000-0000-0000-0000-000000000000"),
                oldClrType: typeof(Guid),
                oldType: "uuid",
                oldNullable: true);
        }

        /// <summary>
        /// Migrations only run forward (ADR 0011); nothing calls this.
        /// </summary>
        protected override void Down(MigrationBuilder migrationBuilder) =>
            throw new NotSupportedException("Migrations only run forward (ADR 0011).");
    }
}
