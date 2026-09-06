using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Hostingaffe.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddBrowserIdentity : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "bootstrap_exchanged_at",
                table: "identity",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "email",
                table: "identity",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "normalized_email",
                table: "identity",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "password_hash",
                table: "identity",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "user_state",
                table: "identity",
                type: "text",
                nullable: true);

            // Cut-two users had no email. Preserve them as active identities
            // with an unmistakably non-deliverable address; an administrator
            // can replace it through the confirmation flow introduced next.
            migrationBuilder.Sql(
                "update identity set email = id::text || '@migration.invalid', " +
                "normalized_email = id::text || '@migration.invalid', user_state = 'active' " +
                "where kind = 'user'");

            migrationBuilder.CreateTable(
                name: "browser_session",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    secret_hash = table.Column<byte[]>(type: "bytea", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    last_used_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    expires_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    revoked_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_browser_session", x => x.id);
                    table.ForeignKey(
                        name: "fk_browser_session_user",
                        column: x => x.user_id,
                        principalTable: "identity",
                        principalColumn: "id");
                });

            migrationBuilder.CreateTable(
                name: "one_time_secret",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    purpose = table.Column<string>(type: "text", nullable: false),
                    secret_hash = table.Column<byte[]>(type: "bytea", nullable: false),
                    pending_email = table.Column<string>(type: "text", nullable: true),
                    pending_normalized_email = table.Column<string>(type: "text", nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    expires_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    used_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_one_time_secret", x => x.id);
                    table.CheckConstraint("ck_one_time_secret_pending_email", "(purpose = 'email_change') = (pending_email is not null)");
                    table.CheckConstraint("ck_one_time_secret_purpose", "purpose in ('invitation', 'password_recovery', 'email_change')");
                    table.ForeignKey(
                        name: "fk_one_time_secret_user",
                        column: x => x.user_id,
                        principalTable: "identity",
                        principalColumn: "id");
                });

            migrationBuilder.CreateIndex(
                name: "identity_email",
                table: "identity",
                column: "normalized_email",
                unique: true,
                filter: "kind = 'user'");

            migrationBuilder.AddCheckConstraint(
                name: "ck_identity_user",
                table: "identity",
                sql: "kind = 'user' and email is not null and normalized_email is not null and user_state in ('invited', 'active', 'deactivated') or kind = 'agent' and email is null and normalized_email is null and user_state is null and password_hash is null and bootstrap_exchanged_at is null");

            migrationBuilder.CreateIndex(
                name: "browser_session_hash",
                table: "browser_session",
                column: "secret_hash",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "browser_session_user",
                table: "browser_session",
                columns: new[] { "user_id", "created_at" },
                descending: new[] { false, true });

            migrationBuilder.CreateIndex(
                name: "one_live_secret_per_purpose",
                table: "one_time_secret",
                columns: new[] { "user_id", "purpose" },
                unique: true,
                filter: "used_at is null");

            migrationBuilder.CreateIndex(
                name: "one_time_secret_hash",
                table: "one_time_secret",
                column: "secret_hash",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "browser_session");

            migrationBuilder.DropTable(
                name: "one_time_secret");

            migrationBuilder.DropIndex(
                name: "identity_email",
                table: "identity");

            migrationBuilder.DropCheckConstraint(
                name: "ck_identity_user",
                table: "identity");

            migrationBuilder.DropColumn(
                name: "bootstrap_exchanged_at",
                table: "identity");

            migrationBuilder.DropColumn(
                name: "email",
                table: "identity");

            migrationBuilder.DropColumn(
                name: "normalized_email",
                table: "identity");

            migrationBuilder.DropColumn(
                name: "password_hash",
                table: "identity");

            migrationBuilder.DropColumn(
                name: "user_state",
                table: "identity");
        }
    }
}
