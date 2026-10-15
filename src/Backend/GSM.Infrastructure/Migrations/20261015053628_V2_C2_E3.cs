using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace GSM.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class V2_C2_E3 : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "instructor_user_id",
                schema: "classes",
                table: "gym_classes",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "instructor_user_id",
                schema: "classes",
                table: "class_sessions",
                type: "uuid",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "exercise_equipment_items",
                schema: "routines",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    gym_id = table.Column<Guid>(type: "uuid", nullable: false),
                    exercise_id = table.Column<Guid>(type: "uuid", nullable: false),
                    equipment_item_id = table.Column<Guid>(type: "uuid", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_exercise_equipment_items", x => x.id);
                    table.ForeignKey(
                        name: "fk_exercise_equipment_items_exercises",
                        column: x => x.exercise_id,
                        principalSchema: "routines",
                        principalTable: "exercises",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "exercise_equipment_models",
                schema: "routines",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    exercise_id = table.Column<Guid>(type: "uuid", nullable: false),
                    model_id = table.Column<Guid>(type: "uuid", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_exercise_equipment_models", x => x.id);
                    table.ForeignKey(
                        name: "fk_exercise_equipment_models_exercises",
                        column: x => x.exercise_id,
                        principalSchema: "routines",
                        principalTable: "exercises",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "ix_gym_classes_gym_id_instructor_user_id",
                schema: "classes",
                table: "gym_classes",
                columns: new[] { "gym_id", "instructor_user_id" });

            migrationBuilder.CreateIndex(
                name: "ix_class_sessions_gym_id_instructor_user_id_session_date",
                schema: "classes",
                table: "class_sessions",
                columns: new[] { "gym_id", "instructor_user_id", "session_date" });

            migrationBuilder.CreateIndex(
                name: "ix_exercise_equipment_items_exercise_id",
                schema: "routines",
                table: "exercise_equipment_items",
                column: "exercise_id");

            migrationBuilder.CreateIndex(
                name: "ix_exercise_equipment_items_gym_id_equipment_item_id",
                schema: "routines",
                table: "exercise_equipment_items",
                columns: new[] { "gym_id", "equipment_item_id" });

            migrationBuilder.CreateIndex(
                name: "ix_exercise_equipment_items_gym_id_exercise_id_equipment_item_",
                schema: "routines",
                table: "exercise_equipment_items",
                columns: new[] { "gym_id", "exercise_id", "equipment_item_id" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_exercise_equipment_models_exercise_id_model_id",
                schema: "routines",
                table: "exercise_equipment_models",
                columns: new[] { "exercise_id", "model_id" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_exercise_equipment_models_model_id",
                schema: "routines",
                table: "exercise_equipment_models",
                column: "model_id");

            // E3: ligas de sistema ejercicio ↔ modelo, por nombre.
            migrationBuilder.SqlScript("E3");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "exercise_equipment_items",
                schema: "routines");

            migrationBuilder.DropTable(
                name: "exercise_equipment_models",
                schema: "routines");

            migrationBuilder.DropIndex(
                name: "ix_gym_classes_gym_id_instructor_user_id",
                schema: "classes",
                table: "gym_classes");

            migrationBuilder.DropIndex(
                name: "ix_class_sessions_gym_id_instructor_user_id_session_date",
                schema: "classes",
                table: "class_sessions");

            migrationBuilder.DropColumn(
                name: "instructor_user_id",
                schema: "classes",
                table: "gym_classes");

            migrationBuilder.DropColumn(
                name: "instructor_user_id",
                schema: "classes",
                table: "class_sessions");
        }
    }
}
