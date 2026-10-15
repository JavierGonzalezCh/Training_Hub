using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace GSM.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class V2_A2_S2_C3_R4 : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "group_number",
                schema: "routines",
                table: "routine_exercises",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "set_targets",
                schema: "routines",
                table: "routine_exercises",
                type: "jsonb",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "member_limit_override",
                schema: "superadmin",
                table: "gym_subscriptions",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "saas_plan_id",
                schema: "superadmin",
                table: "gym_subscriptions",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "strike_at",
                schema: "classes",
                table: "class_bookings",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "void_reason",
                schema: "attendance",
                table: "attendances",
                type: "character varying(500)",
                maxLength: 500,
                nullable: true);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "voided_at",
                schema: "attendance",
                table: "attendances",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "voided_by",
                schema: "attendance",
                table: "attendances",
                type: "uuid",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "booking_blocks",
                schema: "classes",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    gym_id = table.Column<Guid>(type: "uuid", nullable: false),
                    member_id = table.Column<Guid>(type: "uuid", nullable: false),
                    blocked_until = table.Column<DateOnly>(type: "date", nullable: false),
                    strike_count = table.Column<int>(type: "integer", nullable: false),
                    applied_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    lifted_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    lifted_by_user_id = table.Column<Guid>(type: "uuid", nullable: true),
                    lift_reason = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_booking_blocks", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "class_settings",
                schema: "classes",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    gym_id = table.Column<Guid>(type: "uuid", nullable: false),
                    cancel_deadline_hours = table.Column<int>(type: "integer", nullable: false),
                    no_show_limit = table.Column<int>(type: "integer", nullable: false),
                    no_show_window_days = table.Column<int>(type: "integer", nullable: false),
                    no_show_block_days = table.Column<int>(type: "integer", nullable: false),
                    booking_window_days = table.Column<int>(type: "integer", nullable: false),
                    max_active_bookings = table.Column<int>(type: "integer", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_class_settings", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "saas_plans",
                schema: "superadmin",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    name = table.Column<string>(type: "character varying(80)", maxLength: 80, nullable: false),
                    member_limit = table.Column<int>(type: "integer", nullable: true),
                    suggested_price = table.Column<decimal>(type: "numeric(12,2)", nullable: false),
                    is_active = table.Column<bool>(type: "boolean", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_saas_plans", x => x.id);
                });

            migrationBuilder.CreateIndex(
                name: "ix_gym_subscriptions_saas_plan_id",
                schema: "superadmin",
                table: "gym_subscriptions",
                column: "saas_plan_id");

            migrationBuilder.CreateIndex(
                name: "ix_class_bookings_member_strike",
                schema: "classes",
                table: "class_bookings",
                columns: new[] { "member_id", "strike_at" },
                filter: "strike_at IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "ix_booking_blocks_member_id_applied_at",
                schema: "classes",
                table: "booking_blocks",
                columns: new[] { "member_id", "applied_at" });

            migrationBuilder.CreateIndex(
                name: "ix_class_settings_gym_id",
                schema: "classes",
                table: "class_settings",
                column: "gym_id",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_saas_plans_name",
                schema: "superadmin",
                table: "saas_plans",
                column: "name",
                unique: true);

            migrationBuilder.AddForeignKey(
                name: "fk_gym_subscriptions_saas_plans",
                schema: "superadmin",
                table: "gym_subscriptions",
                column: "saas_plan_id",
                principalSchema: "superadmin",
                principalTable: "saas_plans",
                principalColumn: "id",
                onDelete: ReferentialAction.Restrict);

            // S2: siembra los planes del SaaS y enlaza las suscripciones existentes.
            migrationBuilder.SqlScript("S2");

            // R4: copia objetivos y registros a series antes de soltar las columnas viejas.
            migrationBuilder.SqlScript("R4");

            migrationBuilder.DropColumn(
                name: "target_reps",
                schema: "routines",
                table: "routine_exercises");
            migrationBuilder.DropColumn(
                name: "target_sets",
                schema: "routines",
                table: "routine_exercises");
            migrationBuilder.DropColumn(
                name: "target_weight_kg",
                schema: "routines",
                table: "routine_exercises");
            migrationBuilder.DropColumn(
                name: "reps_done",
                schema: "routines",
                table: "routine_exercise_logs");
            migrationBuilder.DropColumn(
                name: "sets_done",
                schema: "routines",
                table: "routine_exercise_logs");
            migrationBuilder.DropColumn(
                name: "weight_kg",
                schema: "routines",
                table: "routine_exercise_logs");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "fk_gym_subscriptions_saas_plans",
                schema: "superadmin",
                table: "gym_subscriptions");

            migrationBuilder.DropTable(
                name: "booking_blocks",
                schema: "classes");

            migrationBuilder.DropTable(
                name: "class_settings",
                schema: "classes");

            migrationBuilder.DropTable(
                name: "saas_plans",
                schema: "superadmin");

            migrationBuilder.DropIndex(
                name: "ix_gym_subscriptions_saas_plan_id",
                schema: "superadmin",
                table: "gym_subscriptions");

            migrationBuilder.DropIndex(
                name: "ix_class_bookings_member_strike",
                schema: "classes",
                table: "class_bookings");

            migrationBuilder.DropColumn(
                name: "group_number",
                schema: "routines",
                table: "routine_exercises");

            migrationBuilder.DropColumn(
                name: "set_targets",
                schema: "routines",
                table: "routine_exercises");

            migrationBuilder.DropColumn(
                name: "member_limit_override",
                schema: "superadmin",
                table: "gym_subscriptions");

            migrationBuilder.DropColumn(
                name: "saas_plan_id",
                schema: "superadmin",
                table: "gym_subscriptions");

            migrationBuilder.DropColumn(
                name: "strike_at",
                schema: "classes",
                table: "class_bookings");

            migrationBuilder.DropColumn(
                name: "void_reason",
                schema: "attendance",
                table: "attendances");

            migrationBuilder.DropColumn(
                name: "voided_at",
                schema: "attendance",
                table: "attendances");

            migrationBuilder.DropColumn(
                name: "voided_by",
                schema: "attendance",
                table: "attendances");

            migrationBuilder.AddColumn<string>(
                name: "target_reps",
                schema: "routines",
                table: "routine_exercises",
                type: "character varying(30)",
                maxLength: 30,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<int>(
                name: "target_sets",
                schema: "routines",
                table: "routine_exercises",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<decimal>(
                name: "target_weight_kg",
                schema: "routines",
                table: "routine_exercises",
                type: "numeric(6,2)",
                precision: 6,
                scale: 2,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "reps_done",
                schema: "routines",
                table: "routine_exercise_logs",
                type: "character varying(30)",
                maxLength: 30,
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "sets_done",
                schema: "routines",
                table: "routine_exercise_logs",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "weight_kg",
                schema: "routines",
                table: "routine_exercise_logs",
                type: "numeric(6,2)",
                precision: 6,
                scale: 2,
                nullable: true);
        }
    }
}
