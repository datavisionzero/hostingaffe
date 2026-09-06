using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;
using NpgsqlTypes;

#nullable disable

namespace Hostingaffe.Infrastructure.Persistence.Migrations
{
    /// <summary>
    /// Everything the instance holds, in one migration.
    /// </summary>
    /// <remarks>
    /// The foundation is a copy of planaffe with its domain cut out, and
    /// planaffe's twelve migrations describe planaffe's schema and carry its
    /// history. Reaching what is left through a chain of drop migrations would
    /// have been archaeology with nothing to show for it: no instance has ever
    /// run this, so there is nothing to migrate. Nothing has been released, and
    /// this is the one occasion there will ever be to start clean — from here
    /// the chain only grows, and only forward (ADR 0011).
    /// </remarks>
    public partial class TheSchemaOfTheFoundation : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "identity",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    name = table.Column<string>(type: "text", nullable: false),
                    administrator = table.Column<bool>(type: "boolean", nullable: false, defaultValue: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    kind = table.Column<string>(type: "text", maxLength: 8, nullable: false),
                    owner_id = table.Column<Guid>(type: "uuid", nullable: true),
                    metadata_reported_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    email = table.Column<string>(type: "text", nullable: true),
                    normalized_email = table.Column<string>(type: "text", nullable: true),
                    user_state = table.Column<string>(type: "text", nullable: true),
                    password_hash = table.Column<string>(type: "text", nullable: true),
                    bootstrap_exchanged_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    metadata = table.Column<string>(type: "jsonb", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_identity", x => x.id);
                    table.CheckConstraint("ck_identity_kind", "kind in ('user', 'agent')");
                    table.CheckConstraint("ck_identity_owner", "kind = 'user' and owner_id is null or kind = 'agent' and owner_id is not null and not administrator");
                    table.CheckConstraint("ck_identity_user", "kind = 'user' and email is not null and normalized_email is not null and user_state in ('invited', 'active', 'deactivated') or kind = 'agent' and email is null and normalized_email is null and user_state is null and password_hash is null and bootstrap_exchanged_at is null");
                    table.ForeignKey(
                        name: "fk_identity_owner",
                        column: x => x.owner_id,
                        principalTable: "identity",
                        principalColumn: "id");
                });

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
                name: "idempotency",
                columns: table => new
                {
                    identity_id = table.Column<Guid>(type: "uuid", nullable: false),
                    key = table.Column<string>(type: "text", nullable: false),
                    request_hash = table.Column<byte[]>(type: "bytea", nullable: false),
                    status = table.Column<short>(type: "smallint", nullable: false),
                    body = table.Column<string>(type: "jsonb", nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_idempotency", x => new { x.identity_id, x.key });
                    table.ForeignKey(
                        name: "fk_idempotency_identity",
                        column: x => x.identity_id,
                        principalTable: "identity",
                        principalColumn: "id");
                });

            migrationBuilder.CreateTable(
                name: "identity_metadata",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    identity_id = table.Column<Guid>(type: "uuid", nullable: false),
                    reported_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    metadata = table.Column<string>(type: "jsonb", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_identity_metadata", x => x.id);
                    table.ForeignKey(
                        name: "fk_identity_metadata_identity",
                        column: x => x.identity_id,
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

            migrationBuilder.CreateTable(
                name: "page",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    slug = table.Column<string>(type: "text", nullable: false),
                    title = table.Column<string>(type: "text", nullable: false),
                    body = table.Column<string>(type: "text", nullable: false, defaultValue: ""),
                    created_by = table.Column<Guid>(type: "uuid", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_by = table.Column<Guid>(type: "uuid", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    deleted_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    deleted_by = table.Column<Guid>(type: "uuid", nullable: true),
                    search = table.Column<NpgsqlTsVector>(type: "tsvector", nullable: true, computedColumnSql: "to_tsvector('simple', title || ' ' || body)", stored: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_page", x => x.id);
                    table.ForeignKey(
                        name: "fk_page_created_by",
                        column: x => x.created_by,
                        principalTable: "identity",
                        principalColumn: "id");
                    table.ForeignKey(
                        name: "fk_page_deleted_by",
                        column: x => x.deleted_by,
                        principalTable: "identity",
                        principalColumn: "id");
                    table.ForeignKey(
                        name: "fk_page_updated_by",
                        column: x => x.updated_by,
                        principalTable: "identity",
                        principalColumn: "id");
                });

            migrationBuilder.CreateTable(
                name: "token",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    identity_id = table.Column<Guid>(type: "uuid", nullable: false),
                    kind = table.Column<string>(type: "text", nullable: false),
                    prefix = table.Column<string>(type: "text", nullable: false),
                    secret_hash = table.Column<byte[]>(type: "bytea", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    revoked_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_token", x => x.id);
                    table.CheckConstraint("ck_token_kind", "kind in ('user', 'agent')");
                    table.ForeignKey(
                        name: "fk_token_identity",
                        column: x => x.identity_id,
                        principalTable: "identity",
                        principalColumn: "id");
                });

            migrationBuilder.CreateTable(
                name: "history",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityAlwaysColumn),
                    page_id = table.Column<Guid>(type: "uuid", nullable: false),
                    actor_id = table.Column<Guid>(type: "uuid", nullable: false),
                    at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    field = table.Column<string>(type: "text", nullable: false),
                    old_value = table.Column<string>(type: "text", nullable: true),
                    new_value = table.Column<string>(type: "text", nullable: true),
                    note = table.Column<string>(type: "text", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_history", x => x.id);
                    table.ForeignKey(
                        name: "fk_history_actor",
                        column: x => x.actor_id,
                        principalTable: "identity",
                        principalColumn: "id");
                    table.ForeignKey(
                        name: "fk_history_page",
                        column: x => x.page_id,
                        principalTable: "page",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

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
                name: "history_page",
                table: "history",
                columns: new[] { "page_id", "id" });

            migrationBuilder.CreateIndex(
                name: "identity_email",
                table: "identity",
                column: "normalized_email",
                unique: true,
                filter: "kind = 'user'");

            migrationBuilder.CreateIndex(
                name: "identity_metadata_identity",
                table: "identity_metadata",
                columns: new[] { "identity_id", "reported_at" });

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

            migrationBuilder.CreateIndex(
                name: "page_search",
                table: "page",
                column: "search")
                .Annotation("Npgsql:IndexMethod", "GIN");

            migrationBuilder.CreateIndex(
                name: "page_slug",
                table: "page",
                column: "slug",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "token_agent",
                table: "token",
                column: "identity_id",
                unique: true,
                filter: "kind = 'agent'");

            migrationBuilder.CreateIndex(
                name: "token_secret_hash",
                table: "token",
                column: "secret_hash",
                unique: true);

            // A name is an address, and two identities whose names differ only
            // in case would be one address meaning two things (docs/storage.md).
            // EF Core has no model for an expression index, so this one is SQL;
            // the model snapshot does not know it, and nothing will ever diff it
            // away. It goes with its table, so the down path needs no line.
            migrationBuilder.Sql("create unique index identity_name on identity (lower(name));");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "browser_session");

            migrationBuilder.DropTable(
                name: "history");

            migrationBuilder.DropTable(
                name: "idempotency");

            migrationBuilder.DropTable(
                name: "identity_metadata");

            migrationBuilder.DropTable(
                name: "one_time_secret");

            migrationBuilder.DropTable(
                name: "token");

            migrationBuilder.DropTable(
                name: "page");

            migrationBuilder.DropTable(
                name: "identity");
        }
    }
}
