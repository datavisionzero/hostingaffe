using Microsoft.EntityFrameworkCore.Migrations;
using NpgsqlTypes;

#nullable disable

namespace Hostingaffe.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddPageSearch : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<NpgsqlTsVector>(
                name: "search",
                table: "page",
                type: "tsvector",
                nullable: true,
                computedColumnSql: "to_tsvector('simple', title || ' ' || body)",
                stored: true);

            migrationBuilder.CreateIndex(
                name: "page_search",
                table: "page",
                column: "search")
                .Annotation("Npgsql:IndexMethod", "GIN");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "page_search",
                table: "page");

            migrationBuilder.DropColumn(
                name: "search",
                table: "page");
        }
    }
}
