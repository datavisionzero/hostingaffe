using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Hostingaffe.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class TheDeviceLogin : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // How `ha login` signs a person in on a machine with no browser
            // (ADR 0005). The rows are short-lived — ten minutes to be
            // approved, a day before the purge takes them — and none of them
            // holds a credential: the device code is here as its hash, and the
            // user code it can read back is what a person types, not what
            // admits them.
            migrationBuilder.CreateTable(
                name: "device_login",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    device_code_hash = table.Column<byte[]>(type: "bytea", nullable: false),
                    user_code = table.Column<string>(type: "character varying(8)", maxLength: 8, nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    expires_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    approved_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    approved_by_user_id = table.Column<Guid>(type: "uuid", nullable: true),
                    denied_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    redeemed_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    issued_token_id = table.Column<Guid>(type: "uuid", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_device_login", x => x.id);
                    table.ForeignKey(
                        name: "fk_device_login_token",
                        column: x => x.issued_token_id,
                        principalTable: "token",
                        principalColumn: "id");
                    table.ForeignKey(
                        name: "fk_device_login_user",
                        column: x => x.approved_by_user_id,
                        principalTable: "identity",
                        principalColumn: "id");
                });

            migrationBuilder.CreateIndex(
                name: "device_login_code_hash",
                table: "device_login",
                column: "device_code_hash",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "device_login_user_code",
                table: "device_login",
                column: "user_code",
                unique: true,
                filter: "approved_at is null and denied_at is null");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "device_login");
        }
    }
}
