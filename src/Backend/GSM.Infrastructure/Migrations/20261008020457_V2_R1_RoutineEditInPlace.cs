using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace GSM.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class V2_R1_RoutineEditInPlace : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "fk_routine_exercise_logs_routine_exercises_routine_exercise_id",
                schema: "routines",
                table: "routine_exercise_logs");

            migrationBuilder.DropIndex(
                name: "ix_routine_days_routine_id_position",
                schema: "routines",
                table: "routine_days");

            migrationBuilder.AddColumn<DateOnly>(
                name: "retired_on",
                schema: "routines",
                table: "routine_exercises",
                type: "date",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "exercise_id",
                schema: "routines",
                table: "routine_exercise_logs",
                type: "uuid",
                nullable: true);

            // Rellena exercise_id de los registros existentes desde su línea (V2_R1.sql) y recién
            // entonces lo vuelve obligatorio.
            migrationBuilder.SqlScript("R1");

            migrationBuilder.AlterColumn<Guid>(
                name: "exercise_id",
                schema: "routines",
                table: "routine_exercise_logs",
                type: "uuid",
                nullable: false,
                oldClrType: typeof(Guid),
                oldType: "uuid",
                oldNullable: true);

            migrationBuilder.AddColumn<DateOnly>(
                name: "retired_on",
                schema: "routines",
                table: "routine_days",
                type: "date",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "ix_routine_exercise_logs_member_id_exercise_id",
                schema: "routines",
                table: "routine_exercise_logs",
                columns: new[] { "member_id", "exercise_id" });

            migrationBuilder.CreateIndex(
                name: "ix_routine_days_routine_id_position",
                schema: "routines",
                table: "routine_days",
                columns: new[] { "routine_id", "position" });

            migrationBuilder.AddForeignKey(
                name: "fk_routine_exercise_logs_routine_exercises_routine_exercise_id",
                schema: "routines",
                table: "routine_exercise_logs",
                column: "routine_exercise_id",
                principalSchema: "routines",
                principalTable: "routine_exercises",
                principalColumn: "id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "fk_routine_exercise_logs_routine_exercises_routine_exercise_id",
                schema: "routines",
                table: "routine_exercise_logs");

            migrationBuilder.DropIndex(
                name: "ix_routine_exercise_logs_member_id_exercise_id",
                schema: "routines",
                table: "routine_exercise_logs");

            migrationBuilder.DropIndex(
                name: "ix_routine_days_routine_id_position",
                schema: "routines",
                table: "routine_days");

            migrationBuilder.DropColumn(
                name: "retired_on",
                schema: "routines",
                table: "routine_exercises");

            migrationBuilder.DropColumn(
                name: "exercise_id",
                schema: "routines",
                table: "routine_exercise_logs");

            migrationBuilder.DropColumn(
                name: "retired_on",
                schema: "routines",
                table: "routine_days");

            migrationBuilder.CreateIndex(
                name: "ix_routine_days_routine_id_position",
                schema: "routines",
                table: "routine_days",
                columns: new[] { "routine_id", "position" },
                unique: true);

            migrationBuilder.AddForeignKey(
                name: "fk_routine_exercise_logs_routine_exercises_routine_exercise_id",
                schema: "routines",
                table: "routine_exercise_logs",
                column: "routine_exercise_id",
                principalSchema: "routines",
                principalTable: "routine_exercises",
                principalColumn: "id",
                onDelete: ReferentialAction.Cascade);
        }
    }
}
