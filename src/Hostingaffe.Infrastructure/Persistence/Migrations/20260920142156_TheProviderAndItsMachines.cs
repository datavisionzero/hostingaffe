using System;
using Microsoft.EntityFrameworkCore.Migrations;
using NpgsqlTypes;

#nullable disable

namespace Hostingaffe.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class TheProviderAndItsMachines : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "ck_history_subject",
                table: "history");

            migrationBuilder.DropCheckConstraint(
                name: "ck_assigned_key_kind",
                table: "assigned_key");

            migrationBuilder.DropIndex(name: "machine_letters", table: "machine");
            migrationBuilder.DropIndex(name: "machine_search", table: "machine");
            migrationBuilder.DropColumn(name: "letters", table: "machine");
            migrationBuilder.DropColumn(name: "search", table: "machine");

            // Preserve the original text exactly before adding the keyed field.
            migrationBuilder.RenameColumn(name: "provider", table: "machine", newName: "legacy_provider");
            migrationBuilder.AddColumn<string>(
                name: "provider", table: "machine", type: "character varying(64)",
                maxLength: 64, nullable: true);

            migrationBuilder.CreateTable(
                name: "provider",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    key = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    description = table.Column<string>(type: "text", nullable: false, defaultValue: ""),
                    created_by = table.Column<Guid>(type: "uuid", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_by = table.Column<Guid>(type: "uuid", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    deleted_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    deleted_by = table.Column<Guid>(type: "uuid", nullable: true),
                    letters = table.Column<string>(type: "text", nullable: true, computedColumnSql: "key || ' ' || name || ' ' || description", stored: true),
                    search = table.Column<NpgsqlTsVector>(type: "tsvector", nullable: true, computedColumnSql: "to_tsvector('simple', key || ' ' || name || ' ' || description)", stored: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_provider", x => x.id);
                    table.UniqueConstraint("AK_provider_key", x => x.key);
                    table.ForeignKey(
                        name: "fk_provider_created_by",
                        column: x => x.created_by,
                        principalTable: "identity",
                        principalColumn: "id");
                    table.ForeignKey(
                        name: "fk_provider_deleted_by",
                        column: x => x.deleted_by,
                        principalTable: "identity",
                        principalColumn: "id");
                    table.ForeignKey(
                        name: "fk_provider_updated_by",
                        column: x => x.updated_by,
                        principalTable: "identity",
                        principalColumn: "id");
                });

            migrationBuilder.Sql("""
                DO $migration$
                DECLARE
                    old_value text;
                    base_key text;
                    chosen_key text;
                    suffix integer;
                    actor uuid;
                    recorded_at timestamptz;
                BEGIN
                    -- Stable ordering makes collision suffixes deterministic.
                    FOR old_value IN
                        SELECT legacy_provider
                        FROM (SELECT DISTINCT legacy_provider FROM machine
                              WHERE legacy_provider IS NOT NULL
                                AND btrim(legacy_provider) <> '') AS originals
                        ORDER BY legacy_provider COLLATE "C"
                    LOOP
                        base_key := trim(both '-' from regexp_replace(
                            lower(btrim(old_value)), '[^a-z0-9]+', '-', 'g'));
                        IF base_key = '' THEN base_key := 'provider'; END IF;
                        base_key := trim(both '-' from left(base_key, 64));
                        chosen_key := base_key;
                        suffix := 2;
                        WHILE EXISTS (SELECT 1 FROM provider WHERE key = chosen_key) LOOP
                            chosen_key := trim(trailing '-' from left(base_key, 64 - length(suffix::text) - 1))
                                || '-' || suffix::text;
                            suffix := suffix + 1;
                        END LOOP;

                        SELECT created_by, created_at INTO actor, recorded_at
                        FROM machine WHERE legacy_provider = old_value
                        ORDER BY created_at, id LIMIT 1;

                        INSERT INTO provider (id, key, name, description,
                                              created_by, created_at, updated_by, updated_at)
                        VALUES (md5('hostingaffe-provider:' || old_value)::uuid,
                                chosen_key, old_value, '', actor, recorded_at, actor, recorded_at);
                        INSERT INTO assigned_key (kind, key) VALUES ('provider', chosen_key);
                    END LOOP;
                END $migration$;
                """);

            migrationBuilder.Sql("""
                UPDATE machine AS m SET provider = p.key
                FROM provider AS p
                WHERE m.legacy_provider = p.name AND m.kind <> 'vm';
                """);

            migrationBuilder.AddColumn<string>(
                name: "letters", table: "machine", type: "text", nullable: true,
                computedColumnSql: "key || ' ' || name || ' ' || coalesce(hostname, '') || ' ' || coalesce(provider, '') || ' ' || coalesce(legacy_provider, '') || ' ' || coalesce(plan, '') || ' ' || coalesce(location, '') || ' ' || coalesce(os, '') || ' ' || coalesce(cpu, '') || ' ' || coalesce(memory, '') || ' ' || coalesce(disk, '') || ' ' || coalesce(ipv4, '') || ' ' || coalesce(ipv6, '') || ' ' || coalesce(private_ip, '') || ' ' || coalesce(ssh, '') || ' ' || description",
                stored: true);
            migrationBuilder.AddColumn<NpgsqlTsVector>(
                name: "search", table: "machine", type: "tsvector", nullable: true,
                computedColumnSql: "to_tsvector('simple', key || ' ' || name || ' ' || coalesce(hostname, '') || ' ' || coalesce(provider, '') || ' ' || coalesce(legacy_provider, '') || ' ' || coalesce(plan, '') || ' ' || coalesce(location, '') || ' ' || coalesce(os, '') || ' ' || coalesce(cpu, '') || ' ' || coalesce(memory, '') || ' ' || coalesce(disk, '') || ' ' || coalesce(ipv4, '') || ' ' || coalesce(ipv6, '') || ' ' || coalesce(private_ip, '') || ' ' || coalesce(ssh, '') || ' ' || description)",
                stored: true);

            migrationBuilder.CreateIndex(
                name: "machine_provider",
                table: "machine",
                column: "provider");

            migrationBuilder.CreateIndex(
                name: "machine_letters",
                table: "machine",
                column: "letters")
                .Annotation("Npgsql:IndexMethod", "GIN")
                .Annotation("Npgsql:IndexOperators", new[] { "gin_trgm_ops" });

            migrationBuilder.CreateIndex(
                name: "machine_search",
                table: "machine",
                column: "search")
                .Annotation("Npgsql:IndexMethod", "GIN");

            migrationBuilder.AddCheckConstraint(
                name: "ck_machine_provider_vm",
                table: "machine",
                sql: "kind <> 'vm' or provider is null");

            migrationBuilder.AddCheckConstraint(
                name: "ck_history_subject",
                table: "history",
                sql: "subject in ('page', 'machine', 'software', 'installation', 'file', 'deployment', 'provider')");

            migrationBuilder.AddCheckConstraint(
                name: "ck_assigned_key_kind",
                table: "assigned_key",
                sql: "kind in ('machine', 'software', 'installation', 'provider')");

            migrationBuilder.CreateIndex(
                name: "provider_key",
                table: "provider",
                column: "key",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "provider_letters",
                table: "provider",
                column: "letters")
                .Annotation("Npgsql:IndexMethod", "GIN")
                .Annotation("Npgsql:IndexOperators", new[] { "gin_trgm_ops" });

            migrationBuilder.CreateIndex(
                name: "provider_search",
                table: "provider",
                column: "search")
                .Annotation("Npgsql:IndexMethod", "GIN");

            migrationBuilder.AddForeignKey(
                name: "fk_machine_provider",
                table: "machine",
                column: "provider",
                principalTable: "provider",
                principalColumn: "key",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder) =>
            throw new NotSupportedException("This migration is forward-only.");
    }
}
