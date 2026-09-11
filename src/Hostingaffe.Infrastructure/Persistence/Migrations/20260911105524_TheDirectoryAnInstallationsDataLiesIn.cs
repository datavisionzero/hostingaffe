using Microsoft.EntityFrameworkCore.Migrations;
using NpgsqlTypes;

#nullable disable

namespace Hostingaffe.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class TheDirectoryAnInstallationsDataLiesIn : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "data",
                table: "installation",
                type: "character varying(500)",
                maxLength: 500,
                nullable: true);

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
                oldComputedColumnSql: "to_tsvector('simple', key || ' ' || name || ' ' || coalesce(path, '') || ' ' || words(urls) || ' ' || words(secrets) || ' ' || description)",
                oldStored: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "data",
                table: "installation");

            migrationBuilder.AlterColumn<NpgsqlTsVector>(
                name: "search",
                table: "installation",
                type: "tsvector",
                nullable: true,
                computedColumnSql: "to_tsvector('simple', key || ' ' || name || ' ' || coalesce(path, '') || ' ' || words(urls) || ' ' || words(secrets) || ' ' || description)",
                stored: true,
                oldClrType: typeof(NpgsqlTsVector),
                oldType: "tsvector",
                oldNullable: true,
                oldComputedColumnSql: "to_tsvector('simple', key || ' ' || name || ' ' || coalesce(path, '') || ' ' || coalesce(data, '') || ' ' || words(urls) || ' ' || words(secrets) || ' ' || description)",
                oldStored: true);
        }
    }
}
