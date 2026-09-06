using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Hostingaffe.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddProjectAccess : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "project_access",
                columns: table => new
                {
                    project_id = table.Column<Guid>(type: "uuid", nullable: false),
                    user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    granted_by = table.Column<Guid>(type: "uuid", nullable: false),
                    granted_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_project_access", x => new { x.project_id, x.user_id });
                    table.ForeignKey(
                        name: "fk_project_access_granted_by",
                        column: x => x.granted_by,
                        principalTable: "identity",
                        principalColumn: "id");
                    table.ForeignKey(
                        name: "fk_project_access_project",
                        column: x => x.project_id,
                        principalTable: "project",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "fk_project_access_user",
                        column: x => x.user_id,
                        principalTable: "identity",
                        principalColumn: "id");
                });

            // Cut three must not revoke access that existed before project
            // assignments did. The first active administrator grants the
            // Cartesian product; all identities selected as recipients are
            // users, never agents.
            migrationBuilder.Sql("""
                insert into project_access (project_id, user_id, granted_by, granted_at)
                select p.id, u.id, administrator.id, now()
                from project p
                cross join identity u
                cross join lateral (
                    select id from identity
                    where kind = 'user' and administrator and user_state = 'active'
                    order by created_at, id limit 1
                ) administrator
                where u.kind = 'user';
                """);

            migrationBuilder.CreateIndex(
                name: "project_access_user",
                table: "project_access",
                column: "user_id");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "project_access");
        }
    }
}
