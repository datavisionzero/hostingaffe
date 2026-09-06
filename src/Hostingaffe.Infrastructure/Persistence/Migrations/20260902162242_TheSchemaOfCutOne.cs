using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace Hostingaffe.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class TheSchemaOfCutOne : Migration
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
                    owner_id = table.Column<Guid>(type: "uuid", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_identity", x => x.id);
                    table.CheckConstraint("ck_identity_kind", "kind in ('user', 'agent')");
                    table.CheckConstraint("ck_identity_owner", "kind = 'user' and owner_id is null or kind = 'agent' and owner_id is not null and not administrator");
                    table.ForeignKey(
                        name: "fk_identity_owner",
                        column: x => x.owner_id,
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
                name: "project",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    key = table.Column<string>(type: "text", nullable: false),
                    name = table.Column<string>(type: "text", nullable: false),
                    triage_required = table.Column<bool>(type: "boolean", nullable: false, defaultValue: false),
                    review_required = table.Column<bool>(type: "boolean", nullable: false, defaultValue: false),
                    last_issue_number = table.Column<int>(type: "integer", nullable: false, defaultValue: 0),
                    last_epic_number = table.Column<int>(type: "integer", nullable: false, defaultValue: 0),
                    created_by = table.Column<Guid>(type: "uuid", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    deleted_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    deleted_by = table.Column<Guid>(type: "uuid", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_project", x => x.id);
                    table.ForeignKey(
                        name: "fk_project_created_by",
                        column: x => x.created_by,
                        principalTable: "identity",
                        principalColumn: "id");
                    table.ForeignKey(
                        name: "fk_project_deleted_by",
                        column: x => x.deleted_by,
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
                name: "epic",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    project_id = table.Column<Guid>(type: "uuid", nullable: false),
                    number = table.Column<int>(type: "integer", nullable: false),
                    title = table.Column<string>(type: "text", nullable: false),
                    description = table.Column<string>(type: "text", nullable: false, defaultValue: ""),
                    status = table.Column<string>(type: "text", nullable: false, defaultValue: "open"),
                    created_by = table.Column<Guid>(type: "uuid", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    closed_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    deleted_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    deleted_by = table.Column<Guid>(type: "uuid", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_epic", x => x.id);
                    table.CheckConstraint("ck_epic_closed", "(status = 'closed') = (closed_at is not null)");
                    table.CheckConstraint("ck_epic_status", "status in ('open', 'closed')");
                    table.ForeignKey(
                        name: "fk_epic_created_by",
                        column: x => x.created_by,
                        principalTable: "identity",
                        principalColumn: "id");
                    table.ForeignKey(
                        name: "fk_epic_deleted_by",
                        column: x => x.deleted_by,
                        principalTable: "identity",
                        principalColumn: "id");
                    table.ForeignKey(
                        name: "fk_epic_project",
                        column: x => x.project_id,
                        principalTable: "project",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "label",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    project_id = table.Column<Guid>(type: "uuid", nullable: false),
                    name = table.Column<string>(type: "text", nullable: false),
                    label_group = table.Column<string>(type: "text", nullable: true),
                    description = table.Column<string>(type: "text", nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    deleted_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_label", x => x.id);
                    table.ForeignKey(
                        name: "fk_label_project",
                        column: x => x.project_id,
                        principalTable: "project",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "issue",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    project_id = table.Column<Guid>(type: "uuid", nullable: false),
                    number = table.Column<int>(type: "integer", nullable: false),
                    title = table.Column<string>(type: "text", nullable: false),
                    description = table.Column<string>(type: "text", nullable: false, defaultValue: ""),
                    result = table.Column<string>(type: "text", nullable: true),
                    status = table.Column<string>(type: "text", nullable: false, defaultValue: "todo"),
                    ready = table.Column<bool>(type: "boolean", nullable: false, defaultValue: false),
                    priority = table.Column<short>(type: "smallint", nullable: false, defaultValue: (short)0),
                    assignee_id = table.Column<Guid>(type: "uuid", nullable: true),
                    epic_id = table.Column<Guid>(type: "uuid", nullable: true),
                    claimed_by = table.Column<Guid>(type: "uuid", nullable: true),
                    claimed_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    claim_extended_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    claim_expires_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    author_id = table.Column<Guid>(type: "uuid", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    closed_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    deleted_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    deleted_by = table.Column<Guid>(type: "uuid", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_issue", x => x.id);
                    table.CheckConstraint("ck_issue_claim_columns", "(claimed_by is null) = (claimed_at is null) and (claimed_by is null) = (claim_extended_at is null)");
                    table.CheckConstraint("ck_issue_claimed", "(status = 'in_progress') = (claimed_by is not null)");
                    table.CheckConstraint("ck_issue_closed", "(status in ('done', 'canceled')) = (closed_at is not null)");
                    table.CheckConstraint("ck_issue_priority", "priority between 0 and 4");
                    table.CheckConstraint("ck_issue_status", "status in ('backlog', 'todo', 'in_progress', 'review', 'done', 'canceled')");
                    table.ForeignKey(
                        name: "fk_issue_assignee",
                        column: x => x.assignee_id,
                        principalTable: "identity",
                        principalColumn: "id");
                    table.ForeignKey(
                        name: "fk_issue_author",
                        column: x => x.author_id,
                        principalTable: "identity",
                        principalColumn: "id");
                    table.ForeignKey(
                        name: "fk_issue_claimed_by",
                        column: x => x.claimed_by,
                        principalTable: "identity",
                        principalColumn: "id");
                    table.ForeignKey(
                        name: "fk_issue_deleted_by",
                        column: x => x.deleted_by,
                        principalTable: "identity",
                        principalColumn: "id");
                    table.ForeignKey(
                        name: "fk_issue_epic",
                        column: x => x.epic_id,
                        principalTable: "epic",
                        principalColumn: "id");
                    table.ForeignKey(
                        name: "fk_issue_project",
                        column: x => x.project_id,
                        principalTable: "project",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "epic_label",
                columns: table => new
                {
                    epic_id = table.Column<Guid>(type: "uuid", nullable: false),
                    label_id = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_epic_label", x => new { x.epic_id, x.label_id });
                    table.ForeignKey(
                        name: "fk_epic_label_epic",
                        column: x => x.epic_id,
                        principalTable: "epic",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "fk_epic_label_label",
                        column: x => x.label_id,
                        principalTable: "label",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "blocker",
                columns: table => new
                {
                    blocker_id = table.Column<Guid>(type: "uuid", nullable: false),
                    blocked_id = table.Column<Guid>(type: "uuid", nullable: false),
                    created_by = table.Column<Guid>(type: "uuid", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_blocker", x => new { x.blocker_id, x.blocked_id });
                    table.CheckConstraint("ck_blocker_not_self", "blocker_id <> blocked_id");
                    table.ForeignKey(
                        name: "fk_blocker_blocked",
                        column: x => x.blocked_id,
                        principalTable: "issue",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "fk_blocker_blocker",
                        column: x => x.blocker_id,
                        principalTable: "issue",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "fk_blocker_created_by",
                        column: x => x.created_by,
                        principalTable: "identity",
                        principalColumn: "id");
                });

            migrationBuilder.CreateTable(
                name: "comment",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    issue_id = table.Column<Guid>(type: "uuid", nullable: false),
                    author_id = table.Column<Guid>(type: "uuid", nullable: false),
                    body = table.Column<string>(type: "text", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_comment", x => x.id);
                    table.ForeignKey(
                        name: "fk_comment_author",
                        column: x => x.author_id,
                        principalTable: "identity",
                        principalColumn: "id");
                    table.ForeignKey(
                        name: "fk_comment_issue",
                        column: x => x.issue_id,
                        principalTable: "issue",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "history",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityAlwaysColumn),
                    issue_id = table.Column<Guid>(type: "uuid", nullable: true),
                    epic_id = table.Column<Guid>(type: "uuid", nullable: true),
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
                    table.CheckConstraint("ck_history_subject", "num_nonnulls(issue_id, epic_id) = 1");
                    table.ForeignKey(
                        name: "fk_history_actor",
                        column: x => x.actor_id,
                        principalTable: "identity",
                        principalColumn: "id");
                    table.ForeignKey(
                        name: "fk_history_epic",
                        column: x => x.epic_id,
                        principalTable: "epic",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "fk_history_issue",
                        column: x => x.issue_id,
                        principalTable: "issue",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "issue_label",
                columns: table => new
                {
                    issue_id = table.Column<Guid>(type: "uuid", nullable: false),
                    label_id = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_issue_label", x => new { x.issue_id, x.label_id });
                    table.ForeignKey(
                        name: "fk_issue_label_issue",
                        column: x => x.issue_id,
                        principalTable: "issue",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "fk_issue_label_label",
                        column: x => x.label_id,
                        principalTable: "label",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "question",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    issue_id = table.Column<Guid>(type: "uuid", nullable: false),
                    question = table.Column<string>(type: "text", nullable: false),
                    asked_by = table.Column<Guid>(type: "uuid", nullable: false),
                    asked_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    answer = table.Column<string>(type: "text", nullable: true),
                    answered_by = table.Column<Guid>(type: "uuid", nullable: true),
                    answered_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_question", x => x.id);
                    table.CheckConstraint("ck_question_answer", "(answer is null) = (answered_by is null) and (answer is null) = (answered_at is null)");
                    table.ForeignKey(
                        name: "fk_question_answered_by",
                        column: x => x.answered_by,
                        principalTable: "identity",
                        principalColumn: "id");
                    table.ForeignKey(
                        name: "fk_question_asked_by",
                        column: x => x.asked_by,
                        principalTable: "identity",
                        principalColumn: "id");
                    table.ForeignKey(
                        name: "fk_question_issue",
                        column: x => x.issue_id,
                        principalTable: "issue",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "blocker_blocked",
                table: "blocker",
                column: "blocked_id");

            migrationBuilder.CreateIndex(
                name: "comment_issue",
                table: "comment",
                columns: new[] { "issue_id", "created_at" });

            migrationBuilder.CreateIndex(
                name: "epic_number",
                table: "epic",
                columns: new[] { "project_id", "number" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "history_epic",
                table: "history",
                columns: new[] { "epic_id", "id" });

            migrationBuilder.CreateIndex(
                name: "history_issue",
                table: "history",
                columns: new[] { "issue_id", "id" });

            migrationBuilder.CreateIndex(
                name: "issue_assignee",
                table: "issue",
                column: "assignee_id",
                filter: "assignee_id is not null");

            migrationBuilder.CreateIndex(
                name: "issue_claim",
                table: "issue",
                column: "claimed_by",
                filter: "claimed_by is not null");

            migrationBuilder.CreateIndex(
                name: "issue_epic",
                table: "issue",
                column: "epic_id",
                filter: "deleted_at is null");

            migrationBuilder.CreateIndex(
                name: "issue_next",
                table: "issue",
                columns: new[] { "project_id", "priority", "created_at", "number" },
                descending: new[] { false, true, false, false },
                filter: "deleted_at is null");

            migrationBuilder.CreateIndex(
                name: "issue_number",
                table: "issue",
                columns: new[] { "project_id", "number" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "issue_updated",
                table: "issue",
                columns: new[] { "project_id", "updated_at", "id" },
                descending: new[] { false, true, false },
                filter: "deleted_at is null");

            migrationBuilder.CreateIndex(
                name: "label_name",
                table: "label",
                columns: new[] { "project_id", "name" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "project_key",
                table: "project",
                column: "key",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "question_issue",
                table: "question",
                columns: new[] { "issue_id", "asked_at" });

            migrationBuilder.CreateIndex(
                name: "question_open",
                table: "question",
                column: "issue_id",
                filter: "answer is null");

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

            // Names are unique across both kinds, case-insensitively, because
            // the API and the CLI address identities by name and a name that
            // could mean two things is no address (docs/storage.md). EF Core
            // has no model for an expression index, so this one is SQL; the
            // model snapshot does not know it, and nothing will ever diff it
            // away.
            migrationBuilder.Sql("create unique index identity_name on identity (lower(name));");

            // The two rules that are derived on read rather than written, in
            // one place every read of an issue goes through (docs/storage.md,
            // What is derived on read): a deleted issue is absent, and an
            // expired claim is no claim, with the status falling back to
            // `todo`. Nothing writes the fallback — the row keeps saying
            // `in_progress` and naming the holder, and the successor's claim
            // writes the one trace the expiry leaves.
            //
            // The model maps it as the keyless `IssueRead`, which the
            // migrations leave alone. Writes do not go through the view; they
            // lock and change the row. A later migration that renames or drops
            // a column the view names has to drop and recreate it, and
            // Postgres will say so.
            migrationBuilder.Sql("""
                create view issue_read as
                select i.id, i.project_id, i.number, i.title, i.description, i.result,
                       case when i.claim_expired then 'todo' else i.status end as status,
                       i.ready, i.priority, i.assignee_id, i.epic_id,
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

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // Postgres will not drop a table a view depends on, and the index
            // goes with its table.
            migrationBuilder.Sql("drop view issue_read;");

            migrationBuilder.DropTable(
                name: "blocker");

            migrationBuilder.DropTable(
                name: "comment");

            migrationBuilder.DropTable(
                name: "epic_label");

            migrationBuilder.DropTable(
                name: "history");

            migrationBuilder.DropTable(
                name: "idempotency");

            migrationBuilder.DropTable(
                name: "issue_label");

            migrationBuilder.DropTable(
                name: "question");

            migrationBuilder.DropTable(
                name: "token");

            migrationBuilder.DropTable(
                name: "label");

            migrationBuilder.DropTable(
                name: "issue");

            migrationBuilder.DropTable(
                name: "epic");

            migrationBuilder.DropTable(
                name: "project");

            migrationBuilder.DropTable(
                name: "identity");
        }
    }
}
