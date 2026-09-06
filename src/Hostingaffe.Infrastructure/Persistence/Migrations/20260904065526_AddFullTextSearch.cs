using Microsoft.EntityFrameworkCore.Migrations;
using NpgsqlTypes;

#nullable disable

namespace Hostingaffe.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddFullTextSearch : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<NpgsqlTsVector>(
                name: "search",
                table: "question",
                type: "tsvector",
                nullable: true,
                computedColumnSql: "to_tsvector('simple', question || ' ' || coalesce(answer, ''))",
                stored: true);

            migrationBuilder.AddColumn<NpgsqlTsVector>(
                name: "search",
                table: "issue",
                type: "tsvector",
                nullable: true,
                computedColumnSql: "to_tsvector('simple', title || ' ' || description || ' ' || coalesce(result, ''))",
                stored: true);

            migrationBuilder.AddColumn<NpgsqlTsVector>(
                name: "search",
                table: "comment",
                type: "tsvector",
                nullable: true,
                computedColumnSql: "to_tsvector('simple', body)",
                stored: true);

            migrationBuilder.CreateIndex(
                name: "question_search",
                table: "question",
                column: "search")
                .Annotation("Npgsql:IndexMethod", "GIN");

            migrationBuilder.CreateIndex(
                name: "issue_search",
                table: "issue",
                column: "search")
                .Annotation("Npgsql:IndexMethod", "GIN");

            migrationBuilder.CreateIndex(
                name: "comment_search",
                table: "comment",
                column: "search")
                .Annotation("Npgsql:IndexMethod", "GIN");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "question_search",
                table: "question");

            migrationBuilder.DropIndex(
                name: "issue_search",
                table: "issue");

            migrationBuilder.DropIndex(
                name: "comment_search",
                table: "comment");

            migrationBuilder.DropColumn(
                name: "search",
                table: "question");

            migrationBuilder.DropColumn(
                name: "search",
                table: "issue");

            migrationBuilder.DropColumn(
                name: "search",
                table: "comment");
        }
    }
}
