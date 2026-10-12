using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace GSM.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class V2_I7_KioskDevices : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "kiosk_devices",
                schema: "identity",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    gym_id = table.Column<Guid>(type: "uuid", nullable: false),
                    name = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    paired_by = table.Column<Guid>(type: "uuid", nullable: false),
                    pairing_code_hash = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    pairing_code_expires_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    paired_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    revoked_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    refresh_token_hash = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_kiosk_devices", x => x.id);
                });

            migrationBuilder.CreateIndex(
                name: "ix_kiosk_devices_gym_id",
                schema: "identity",
                table: "kiosk_devices",
                column: "gym_id");

            migrationBuilder.CreateIndex(
                name: "ix_kiosk_devices_pairing_code_hash",
                schema: "identity",
                table: "kiosk_devices",
                column: "pairing_code_hash",
                unique: true,
                filter: "pairing_code_hash IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "ix_kiosk_devices_refresh_token_hash",
                schema: "identity",
                table: "kiosk_devices",
                column: "refresh_token_hash",
                unique: true,
                filter: "refresh_token_hash IS NOT NULL");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "kiosk_devices",
                schema: "identity");
        }
    }
}
