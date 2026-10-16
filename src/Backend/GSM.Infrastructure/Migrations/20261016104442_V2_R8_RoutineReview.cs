using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace GSM.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class V2_R8_RoutineReview : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "last_reviewed_at",
                schema: "routines",
                table: "routines",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "last_reviewed_by",
                schema: "routines",
                table: "routines",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<DateOnly>(
                name: "review_on",
                schema: "routines",
                table: "routines",
                type: "date",
                nullable: false,
                defaultValue: new DateOnly(1, 1, 1));

            migrationBuilder.CreateTable(
                name: "routine_settings",
                schema: "routines",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    gym_id = table.Column<Guid>(type: "uuid", nullable: false),
                    review_weeks = table.Column<int>(type: "integer", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_routine_settings", x => x.id);
                });

            migrationBuilder.CreateIndex(
                name: "ix_routines_gym_id_is_active_review_on",
                schema: "routines",
                table: "routines",
                columns: new[] { "gym_id", "is_active", "review_on" });

            migrationBuilder.CreateIndex(
                name: "ix_routine_settings_gym_id",
                schema: "routines",
                table: "routine_settings",
                column: "gym_id",
                unique: true);

            migrationBuilder.SqlScript("R8");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "routine_settings",
                schema: "routines");

            migrationBuilder.DropIndex(
                name: "ix_routines_gym_id_is_active_review_on",
                schema: "routines",
                table: "routines");

            migrationBuilder.DropColumn(
                name: "last_reviewed_at",
                schema: "routines",
                table: "routines");

            migrationBuilder.DropColumn(
                name: "last_reviewed_by",
                schema: "routines",
                table: "routines");

            migrationBuilder.DropColumn(
                name: "review_on",
                schema: "routines",
                table: "routines");
        }
    }
}
