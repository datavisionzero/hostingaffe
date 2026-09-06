using Microsoft.EntityFrameworkCore.Migrations;
using NpgsqlTypes;

#nullable disable

namespace Hostingaffe.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class TheSearch : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // The one function the schema owns. It exists because Postgres
            // marks `array_to_string` stable rather than immutable, and a
            // generated column takes only immutable expressions — while for a
            // `text[]` and a constant separator the result depends on nothing
            // at all. Without it an installation's `urls` and `secrets` would
            // be the two fields nobody could search for.
            migrationBuilder.Sql("""
                create function words(value text[]) returns text
                    language sql
                    immutable
                    parallel safe
                    returns null on null input
                    return array_to_string(value, ' ');
                """);

            migrationBuilder.AddColumn<NpgsqlTsVector>(
                name: "search",
                table: "software",
                type: "tsvector",
                nullable: true,
                computedColumnSql: "to_tsvector('simple', key || ' ' || name || ' ' || coalesce(image, '') || ' ' || coalesce(homepage, '') || ' ' || coalesce(repository, '') || ' ' || description)",
                stored: true);

            migrationBuilder.AddColumn<NpgsqlTsVector>(
                name: "search",
                table: "machine",
                type: "tsvector",
                nullable: true,
                computedColumnSql: "to_tsvector('simple', key || ' ' || name || ' ' || coalesce(hostname, '') || ' ' || coalesce(provider, '') || ' ' || coalesce(plan, '') || ' ' || coalesce(location, '') || ' ' || coalesce(os, '') || ' ' || coalesce(cpu, '') || ' ' || coalesce(memory, '') || ' ' || coalesce(disk, '') || ' ' || coalesce(ipv4, '') || ' ' || coalesce(ipv6, '') || ' ' || coalesce(private_ip, '') || ' ' || coalesce(ssh, '') || ' ' || description)",
                stored: true);

            migrationBuilder.AddColumn<NpgsqlTsVector>(
                name: "search",
                table: "installation",
                type: "tsvector",
                nullable: true,
                computedColumnSql: "to_tsvector('simple', key || ' ' || name || ' ' || coalesce(path, '') || ' ' || words(urls) || ' ' || words(secrets) || ' ' || description)",
                stored: true);

            migrationBuilder.AddColumn<NpgsqlTsVector>(
                name: "search",
                table: "file_revision",
                type: "tsvector",
                nullable: true,
                computedColumnSql: "to_tsvector('simple', content)",
                stored: true);

            migrationBuilder.AddColumn<NpgsqlTsVector>(
                name: "search",
                table: "file",
                type: "tsvector",
                nullable: true,
                computedColumnSql: "to_tsvector('simple', path)",
                stored: true);

            migrationBuilder.AddColumn<NpgsqlTsVector>(
                name: "search",
                table: "deployment",
                type: "tsvector",
                nullable: true,
                computedColumnSql: "to_tsvector('simple', version || ' ' || coalesce(\"ref\", '') || ' ' || coalesce(ticket, '') || ' ' || note)",
                stored: true);

            migrationBuilder.CreateIndex(
                name: "software_search",
                table: "software",
                column: "search")
                .Annotation("Npgsql:IndexMethod", "GIN");

            migrationBuilder.CreateIndex(
                name: "machine_search",
                table: "machine",
                column: "search")
                .Annotation("Npgsql:IndexMethod", "GIN");

            migrationBuilder.CreateIndex(
                name: "installation_search",
                table: "installation",
                column: "search")
                .Annotation("Npgsql:IndexMethod", "GIN");

            migrationBuilder.CreateIndex(
                name: "file_revision_search",
                table: "file_revision",
                column: "search")
                .Annotation("Npgsql:IndexMethod", "GIN");

            migrationBuilder.CreateIndex(
                name: "file_search",
                table: "file",
                column: "search")
                .Annotation("Npgsql:IndexMethod", "GIN");

            migrationBuilder.CreateIndex(
                name: "deployment_search",
                table: "deployment",
                column: "search")
                .Annotation("Npgsql:IndexMethod", "GIN");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "software_search",
                table: "software");

            migrationBuilder.DropIndex(
                name: "machine_search",
                table: "machine");

            migrationBuilder.DropIndex(
                name: "installation_search",
                table: "installation");

            migrationBuilder.DropIndex(
                name: "file_revision_search",
                table: "file_revision");

            migrationBuilder.DropIndex(
                name: "file_search",
                table: "file");

            migrationBuilder.DropIndex(
                name: "deployment_search",
                table: "deployment");

            migrationBuilder.DropColumn(
                name: "search",
                table: "software");

            migrationBuilder.DropColumn(
                name: "search",
                table: "machine");

            migrationBuilder.DropColumn(
                name: "search",
                table: "installation");

            migrationBuilder.DropColumn(
                name: "search",
                table: "file_revision");

            migrationBuilder.DropColumn(
                name: "search",
                table: "file");

            migrationBuilder.DropColumn(
                name: "search",
                table: "deployment");

            migrationBuilder.Sql("drop function words(text[])");
        }
    }
}
