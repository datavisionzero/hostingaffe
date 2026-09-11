using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Hostingaffe.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class ThePathIsFoundByItsLetters : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Postgres makes one word of `/srv/caddy/caddy.env`, so a piece of
            // a path matched nothing at all (ADR 0012). Beside every stored
            // `tsvector` there is now a trigram index over the same text: a
            // `letters` column where that text is a concatenation, and the
            // column itself where it is a text already — a file's content, a
            // page's title and body.
            //
            // A file is the one place where the two are made of different text.
            // As a word it stays its path under its owner; as letters it is the
            // whole place it lies on the machine, which is the only column that
            // carries a machine file's directory at all.
            //
            // `pg_trgm` is trusted, so the database owner creates it and no
            // instance needs a superuser to migrate.
            migrationBuilder.AlterDatabase()
                .Annotation("Npgsql:PostgresExtension:pg_trgm", ",,");

            migrationBuilder.AddColumn<string>(
                name: "letters",
                table: "software",
                type: "text",
                nullable: true,
                computedColumnSql: "key || ' ' || name || ' ' || coalesce(image, '') || ' ' || coalesce(homepage, '') || ' ' || coalesce(repository, '') || ' ' || description",
                stored: true);

            migrationBuilder.AddColumn<string>(
                name: "letters",
                table: "machine",
                type: "text",
                nullable: true,
                computedColumnSql: "key || ' ' || name || ' ' || coalesce(hostname, '') || ' ' || coalesce(provider, '') || ' ' || coalesce(plan, '') || ' ' || coalesce(location, '') || ' ' || coalesce(os, '') || ' ' || coalesce(cpu, '') || ' ' || coalesce(memory, '') || ' ' || coalesce(disk, '') || ' ' || coalesce(ipv4, '') || ' ' || coalesce(ipv6, '') || ' ' || coalesce(private_ip, '') || ' ' || coalesce(ssh, '') || ' ' || description",
                stored: true);

            migrationBuilder.AddColumn<string>(
                name: "letters",
                table: "installation_secret",
                type: "text",
                nullable: true,
                computedColumnSql: "name || ' ' || coalesce(path, '')",
                stored: true);

            migrationBuilder.AddColumn<string>(
                name: "letters",
                table: "installation",
                type: "text",
                nullable: true,
                computedColumnSql: "key || ' ' || name || ' ' || coalesce(path, '') || ' ' || coalesce(data, '') || ' ' || words(urls) || ' ' || description",
                stored: true);

            migrationBuilder.AddColumn<string>(
                name: "letters",
                table: "file",
                type: "text",
                nullable: true,
                computedColumnSql: "coalesce(rtrim(directory, '/') || '/', '') || path",
                stored: true);

            migrationBuilder.AddColumn<string>(
                name: "letters",
                table: "deployment",
                type: "text",
                nullable: true,
                computedColumnSql: "version || ' ' || coalesce(\"ref\", '') || ' ' || coalesce(ticket, '') || ' ' || note",
                stored: true);

            migrationBuilder.CreateIndex(
                name: "software_letters",
                table: "software",
                column: "letters")
                .Annotation("Npgsql:IndexMethod", "GIN")
                .Annotation("Npgsql:IndexOperators", new[] { "gin_trgm_ops" });

            migrationBuilder.CreateIndex(
                name: "page_body_letters",
                table: "page",
                column: "body")
                .Annotation("Npgsql:IndexMethod", "GIN")
                .Annotation("Npgsql:IndexOperators", new[] { "gin_trgm_ops" });

            migrationBuilder.CreateIndex(
                name: "page_title_letters",
                table: "page",
                column: "title")
                .Annotation("Npgsql:IndexMethod", "GIN")
                .Annotation("Npgsql:IndexOperators", new[] { "gin_trgm_ops" });

            migrationBuilder.CreateIndex(
                name: "machine_letters",
                table: "machine",
                column: "letters")
                .Annotation("Npgsql:IndexMethod", "GIN")
                .Annotation("Npgsql:IndexOperators", new[] { "gin_trgm_ops" });

            migrationBuilder.CreateIndex(
                name: "installation_secret_letters",
                table: "installation_secret",
                column: "letters")
                .Annotation("Npgsql:IndexMethod", "GIN")
                .Annotation("Npgsql:IndexOperators", new[] { "gin_trgm_ops" });

            migrationBuilder.CreateIndex(
                name: "installation_letters",
                table: "installation",
                column: "letters")
                .Annotation("Npgsql:IndexMethod", "GIN")
                .Annotation("Npgsql:IndexOperators", new[] { "gin_trgm_ops" });

            migrationBuilder.CreateIndex(
                name: "file_revision_letters",
                table: "file_revision",
                column: "content")
                .Annotation("Npgsql:IndexMethod", "GIN")
                .Annotation("Npgsql:IndexOperators", new[] { "gin_trgm_ops" });

            migrationBuilder.CreateIndex(
                name: "file_letters",
                table: "file",
                column: "letters")
                .Annotation("Npgsql:IndexMethod", "GIN")
                .Annotation("Npgsql:IndexOperators", new[] { "gin_trgm_ops" });

            migrationBuilder.CreateIndex(
                name: "deployment_letters",
                table: "deployment",
                column: "letters")
                .Annotation("Npgsql:IndexMethod", "GIN")
                .Annotation("Npgsql:IndexOperators", new[] { "gin_trgm_ops" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "software_letters",
                table: "software");

            migrationBuilder.DropIndex(
                name: "page_body_letters",
                table: "page");

            migrationBuilder.DropIndex(
                name: "page_title_letters",
                table: "page");

            migrationBuilder.DropIndex(
                name: "machine_letters",
                table: "machine");

            migrationBuilder.DropIndex(
                name: "installation_secret_letters",
                table: "installation_secret");

            migrationBuilder.DropIndex(
                name: "installation_letters",
                table: "installation");

            migrationBuilder.DropIndex(
                name: "file_revision_letters",
                table: "file_revision");

            migrationBuilder.DropIndex(
                name: "file_letters",
                table: "file");

            migrationBuilder.DropIndex(
                name: "deployment_letters",
                table: "deployment");

            migrationBuilder.DropColumn(
                name: "letters",
                table: "software");

            migrationBuilder.DropColumn(
                name: "letters",
                table: "machine");

            migrationBuilder.DropColumn(
                name: "letters",
                table: "installation_secret");

            migrationBuilder.DropColumn(
                name: "letters",
                table: "installation");

            migrationBuilder.DropColumn(
                name: "letters",
                table: "file");

            migrationBuilder.DropColumn(
                name: "letters",
                table: "deployment");

            migrationBuilder.AlterDatabase()
                .OldAnnotation("Npgsql:PostgresExtension:pg_trgm", ",,");
        }
    }
}
