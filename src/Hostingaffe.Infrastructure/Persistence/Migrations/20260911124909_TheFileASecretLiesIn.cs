using System;
using Microsoft.EntityFrameworkCore.Migrations;
using NpgsqlTypes;

#nullable disable

namespace Hostingaffe.Infrastructure.Persistence.Migrations
{
    /// <summary>
    /// A secret stops being a word and becomes a row that also says which file
    /// on the machine its value lies in (ADR 0011). The names an instance
    /// already holds are carried over before the column they were in goes: the
    /// migration runs forward, and nothing is retyped by hand.
    /// </summary>
    /// <inheritdoc />
    public partial class TheFileASecretLiesIn : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "installation_secret",
                columns: table => new
                {
                    name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    installation_id = table.Column<Guid>(type: "uuid", nullable: false),
                    path = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    search = table.Column<NpgsqlTsVector>(type: "tsvector", nullable: true, computedColumnSql: "to_tsvector('simple', name || ' ' || coalesce(path, ''))", stored: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_installation_secret", x => new { x.installation_id, x.name });
                    table.ForeignKey(
                        name: "fk_installation_secret_installation",
                        column: x => x.installation_id,
                        principalTable: "installation",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "installation_secret_search",
                table: "installation_secret",
                column: "search")
                .Annotation("Npgsql:IndexMethod", "GIN");

            // Every name that was in the array becomes a row saying nothing yet
            // about where it lies — which is exactly what the record knew.
            // `distinct` because the array is a column and a column promises
            // nothing the write path enforced.
            migrationBuilder.Sql(
                """
                insert into installation_secret (installation_id, name)
                select distinct id, unnest(secrets) from installation;
                """);

            // The vector goes before the column it reads: Postgres will not drop
            // a column a generated one depends on.
            migrationBuilder.AlterColumn<NpgsqlTsVector>(
                name: "search",
                table: "installation",
                type: "tsvector",
                nullable: true,
                computedColumnSql: "to_tsvector('simple', key || ' ' || name || ' ' || coalesce(path, '') || ' ' || coalesce(data, '') || ' ' || words(urls) || ' ' || description)",
                stored: true,
                oldClrType: typeof(NpgsqlTsVector),
                oldType: "tsvector",
                oldNullable: true,
                oldComputedColumnSql: "to_tsvector('simple', key || ' ' || name || ' ' || coalesce(path, '') || ' ' || coalesce(data, '') || ' ' || words(urls) || ' ' || words(secrets) || ' ' || description)",
                oldStored: true);

            migrationBuilder.DropColumn(
                name: "secrets",
                table: "installation");
        }

        /// <summary>
        /// Back to the array, with the names and without the files: a column of
        /// words has nowhere to put the half this migration added.
        /// </summary>
        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string[]>(
                name: "secrets",
                table: "installation",
                type: "text[]",
                nullable: false,
                defaultValue: new string[0]);

            migrationBuilder.Sql(
                """
                update installation
                   set secrets = coalesce(
                           (select array_agg(s.name order by s.name)
                              from installation_secret s
                             where s.installation_id = installation.id),
                           '{}');
                """);

            migrationBuilder.AlterColumn<NpgsqlTsVector>(
                name: "search",
                table: "installation",
                type: "tsvector",
                nullable: true,
                computedColumnSql: "to_tsvector('simple', key || ' ' || name || ' ' || coalesce(path, '') || ' ' || coalesce(data, '') || ' ' || words(urls) || ' ' || words(secrets) || ' ' || description)",
                stored: true,
                oldClrType: typeof(NpgsqlTsVector),
                oldType: "tsvector",
                oldNullable: true,
                oldComputedColumnSql: "to_tsvector('simple', key || ' ' || name || ' ' || coalesce(path, '') || ' ' || coalesce(data, '') || ' ' || words(urls) || ' ' || description)",
                oldStored: true);

            migrationBuilder.DropTable(
                name: "installation_secret");
        }
    }
}
