using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Hostingaffe.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddReleases : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "release",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    project_id = table.Column<Guid>(type: "uuid", nullable: false),
                    name = table.Column<string>(type: "text", nullable: true),
                    description = table.Column<string>(type: "text", nullable: false, defaultValue: ""),
                    status = table.Column<string>(type: "text", nullable: false, defaultValue: "open"),
                    published_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    published_by = table.Column<Guid>(type: "uuid", nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_release", x => x.id);
                    table.CheckConstraint("ck_release_published", "(status = 'published') = (name is not null) and (status = 'published') = (published_at is not null) and (status = 'published') = (published_by is not null)");
                    table.ForeignKey(
                        name: "fk_release_project",
                        column: x => x.project_id,
                        principalTable: "project",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "fk_release_published_by",
                        column: x => x.published_by,
                        principalTable: "identity",
                        principalColumn: "id");
                });

            migrationBuilder.CreateTable(
                name: "release_issue",
                columns: table => new
                {
                    release_id = table.Column<Guid>(type: "uuid", nullable: false),
                    issue_id = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_release_issue", x => new { x.release_id, x.issue_id });
                    table.ForeignKey(
                        name: "fk_release_issue_issue",
                        column: x => x.issue_id,
                        principalTable: "issue",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "fk_release_issue_release",
                        column: x => x.release_id,
                        principalTable: "release",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "release_open",
                table: "release",
                column: "project_id",
                unique: true,
                filter: "status = 'open'");

            migrationBuilder.CreateIndex(
                name: "release_issue_issue",
                table: "release_issue",
                column: "issue_id");

            migrationBuilder.Sql("""
                create unique index release_name on release (project_id, lower(name)) where name is not null;

                insert into release (id, project_id, description, status, created_at, updated_at)
                select gen_random_uuid(), p.id, '', 'open', now(), now() from project p;

                insert into release_issue (release_id, issue_id)
                select r.id, i.id
                  from release r
                  join issue i on i.project_id = r.project_id
                 where r.status = 'open' and i.status = 'done' and i.deleted_at is null
                   and (i.parent_id is null or exists (
                       select 1 from issue p where p.id = i.parent_id and p.status = 'done' and p.deleted_at is null));
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "release_issue");

            migrationBuilder.DropTable(
                name: "release");
        }
    }
}
