using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Hostingaffe.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddSubIssues : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("drop view issue_read;");

            migrationBuilder.AddColumn<Guid>(
                name: "parent_id",
                table: "issue",
                type: "uuid",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "issue_parent",
                table: "issue",
                column: "parent_id",
                filter: "parent_id is not null");

            migrationBuilder.AddCheckConstraint(
                name: "ck_issue_parent_not_self",
                table: "issue",
                sql: "parent_id is distinct from id");

            migrationBuilder.AddForeignKey(
                name: "fk_issue_parent",
                table: "issue",
                column: "parent_id",
                principalTable: "issue",
                principalColumn: "id");

            CreateReadView(migrationBuilder, includeParent: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("drop view issue_read;");

            migrationBuilder.DropForeignKey(
                name: "fk_issue_parent",
                table: "issue");

            migrationBuilder.DropIndex(
                name: "issue_parent",
                table: "issue");

            migrationBuilder.DropCheckConstraint(
                name: "ck_issue_parent_not_self",
                table: "issue");

            migrationBuilder.DropColumn(
                name: "parent_id",
                table: "issue");

            CreateReadView(migrationBuilder, includeParent: false);
        }

        private static void CreateReadView(MigrationBuilder migrationBuilder, bool includeParent)
        {
            var parent = includeParent ? ", i.parent_id" : string.Empty;
            migrationBuilder.Sql($$"""
                create view issue_read as
                select i.id, i.project_id, i.number, i.title, i.description, i.result,
                       case when i.claim_expired then 'todo' else i.status end as status,
                       i.ready, i.priority, i.assignee_id, i.epic_id{{parent}},
                       case when i.claim_expired then null else i.claimed_by end        as claimed_by,
                       case when i.claim_expired then null else i.claimed_at end        as claimed_at,
                       case when i.claim_expired then null else i.claim_expires_at end  as claim_expires_at,
                       i.author_id, i.created_at, i.updated_at, i.closed_at
                  from (select *,
                               claimed_by is not null
                           and claim_expires_at is not null
                           and claim_expires_at <= now() as claim_expired
                          from issue
                         where deleted_at is null) i;
                """);
        }
    }
}
