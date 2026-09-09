using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace GSM.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class Etapa09_5_StreakFreezes : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "streak_freezes",
                schema: "achievements",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    gym_id = table.Column<Guid>(type: "uuid", nullable: false),
                    member_id = table.Column<Guid>(type: "uuid", nullable: false),
                    granted_on = table.Column<DateOnly>(type: "date", nullable: false),
                    used_on = table.Column<DateOnly>(type: "date", nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_streak_freezes", x => x.id);
                });

            migrationBuilder.CreateIndex(
                name: "ix_streak_freezes_member_id_granted_on",
                schema: "achievements",
                table: "streak_freezes",
                columns: new[] { "member_id", "granted_on" });

            migrationBuilder.CreateIndex(
                name: "ix_streak_freezes_member_id_used_on_spent",
                schema: "achievements",
                table: "streak_freezes",
                columns: new[] { "member_id", "used_on" },
                unique: true,
                filter: "used_on IS NOT NULL");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "streak_freezes",
                schema: "achievements");
        }
    }
}
