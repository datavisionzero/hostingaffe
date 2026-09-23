using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Hostingaffe.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class TheMachineHasAPicture : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "avatar",
                table: "machine",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "avatar_color",
                table: "machine",
                type: "text",
                nullable: true);

            migrationBuilder.AddCheckConstraint(
                name: "ck_machine_avatar",
                table: "machine",
                sql: "avatar is null or avatar in ('monkey', 'gorilla', 'sloth', 'raccoon', 'fox', 'owl', 'penguin', 'octopus', 'cat', 'frog', 'bear', 'wolf', 'lion', 'puma', 'robot', 'rack', 'turbo', 'tower', 'minipc', 'minimac', 'desktop', 'laptop', 'devbook', 'aibox', 'monitor', 'router', 'proxy', 'signpost', 'firewall', 'cloud', 'container', 'database', 'harddrive', 'bucket', 'floppy', 'tape', 'logbook', 'gauge')");

            migrationBuilder.AddCheckConstraint(
                name: "ck_machine_avatar_color",
                table: "machine",
                sql: "avatar_color is null or avatar_color in ('brown', 'slate', 'teal', 'orange', 'berry', 'sage', 'blue', 'red', 'mustard', 'lavender')");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "ck_machine_avatar",
                table: "machine");

            migrationBuilder.DropCheckConstraint(
                name: "ck_machine_avatar_color",
                table: "machine");

            migrationBuilder.DropColumn(
                name: "avatar",
                table: "machine");

            migrationBuilder.DropColumn(
                name: "avatar_color",
                table: "machine");
        }
    }
}
