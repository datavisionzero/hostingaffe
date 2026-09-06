using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Hostingaffe.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddWakeUps : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "project_id",
                table: "question",
                type: "uuid",
                nullable: true);

            migrationBuilder.Sql("""
                update question q
                   set project_id = i.project_id
                  from issue i
                 where i.id = q.issue_id;
                """);

            migrationBuilder.AlterColumn<Guid>(
                name: "project_id",
                table: "question",
                type: "uuid",
                nullable: false,
                oldClrType: typeof(Guid),
                oldType: "uuid",
                oldNullable: true);

            migrationBuilder.AddForeignKey(
                name: "fk_question_project",
                table: "question",
                column: "project_id",
                principalTable: "project",
                principalColumn: "id",
                onDelete: ReferentialAction.Cascade);

            migrationBuilder.Sql("""
                create function hostingaffe_notify() returns trigger language plpgsql as $$
                begin
                    perform pg_notify('hostingaffe_' || replace(new.project_id::text, '-', ''), '');
                    return null;
                end $$;

                create trigger issue_notify
                    after insert or update on issue
                    for each row execute function hostingaffe_notify();

                create trigger question_notify
                    after insert or update on question
                    for each row execute function hostingaffe_notify();
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
                drop trigger question_notify on question;
                drop trigger issue_notify on issue;
                drop function hostingaffe_notify();
                """);

            migrationBuilder.DropForeignKey(
                name: "fk_question_project",
                table: "question");

            migrationBuilder.DropColumn(
                name: "project_id",
                table: "question");
        }
    }
}
