using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace GSM.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class Etapa09_3_RoutineLogs : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "routine_exercise_logs",
                schema: "routines",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    gym_id = table.Column<Guid>(type: "uuid", nullable: false),
                    member_id = table.Column<Guid>(type: "uuid", nullable: false),
                    routine_exercise_id = table.Column<Guid>(type: "uuid", nullable: false),
                    performed_on = table.Column<DateOnly>(type: "date", nullable: false),
                    completed_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    sets_done = table.Column<int>(type: "integer", nullable: true),
                    reps_done = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: true),
                    weight_kg = table.Column<decimal>(type: "numeric(6,2)", precision: 6, scale: 2, nullable: true),
                    notes = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_routine_exercise_logs", x => x.id);
                    table.ForeignKey(
                        name: "fk_routine_exercise_logs_routine_exercises_routine_exercise_id",
                        column: x => x.routine_exercise_id,
                        principalSchema: "routines",
                        principalTable: "routine_exercises",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "ix_routine_exercise_logs_member_id_performed_on",
                schema: "routines",
                table: "routine_exercise_logs",
                columns: new[] { "member_id", "performed_on" });

            migrationBuilder.CreateIndex(
                name: "ix_routine_exercise_logs_routine_exercise_id_performed_on",
                schema: "routines",
                table: "routine_exercise_logs",
                columns: new[] { "routine_exercise_id", "performed_on" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "routine_exercise_logs",
                schema: "routines");
        }
    }
}
