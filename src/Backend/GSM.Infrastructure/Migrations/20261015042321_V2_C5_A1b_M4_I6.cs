using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace GSM.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class V2_C5_A1b_M4_I6 : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "ix_attendances_member_id_attendance_date",
                schema: "attendance",
                table: "attendances");

            migrationBuilder.AddColumn<DateOnly>(
                name: "ends_on",
                schema: "classes",
                table: "gym_classes",
                type: "date",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "email_change_requests",
                schema: "identity",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    new_email = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false),
                    code_hash = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    issued_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    expires_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    attempts = table.Column<int>(type: "integer", nullable: false),
                    consumed_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_email_change_requests", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "member_entry_rules",
                schema: "attendance",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    gym_id = table.Column<Guid>(type: "uuid", nullable: false),
                    member_id = table.Column<Guid>(type: "uuid", nullable: false),
                    max_daily_entries = table.Column<int>(type: "integer", nullable: false),
                    reason = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false),
                    valid_until = table.Column<DateOnly>(type: "date", nullable: true),
                    granted_by = table.Column<Guid>(type: "uuid", nullable: false),
                    granted_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_member_entry_rules", x => x.id);
                    table.CheckConstraint("ck_member_entry_rules_max_daily_entries", "max_daily_entries BETWEEN 2 AND 5");
                });

            migrationBuilder.CreateTable(
                name: "member_measurements",
                schema: "members",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    gym_id = table.Column<Guid>(type: "uuid", nullable: false),
                    member_id = table.Column<Guid>(type: "uuid", nullable: false),
                    measured_on = table.Column<DateOnly>(type: "date", nullable: false),
                    weight_kg = table.Column<decimal>(type: "numeric(5,2)", precision: 5, scale: 2, nullable: false),
                    height_cm = table.Column<decimal>(type: "numeric(5,1)", precision: 5, scale: 1, nullable: true),
                    recorded_by_user_id = table.Column<Guid>(type: "uuid", nullable: true),
                    recorded_by_role = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_member_measurements", x => x.id);
                    table.CheckConstraint("ck_member_measurements_height_cm", "height_cm IS NULL OR (height_cm >= 50 AND height_cm <= 250)");
                    table.CheckConstraint("ck_member_measurements_weight_kg", "weight_kg >= 20 AND weight_kg <= 400");
                    table.ForeignKey(
                        name: "fk_member_measurements_members_member_id",
                        column: x => x.member_id,
                        principalSchema: "members",
                        principalTable: "members",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "ix_attendances_member_id_attendance_date",
                schema: "attendance",
                table: "attendances",
                columns: new[] { "member_id", "attendance_date" });

            migrationBuilder.CreateIndex(
                name: "ix_email_change_requests_user_id_issued_at",
                schema: "identity",
                table: "email_change_requests",
                columns: new[] { "user_id", "issued_at" });

            migrationBuilder.CreateIndex(
                name: "ix_member_entry_rules_gym_id",
                schema: "attendance",
                table: "member_entry_rules",
                column: "gym_id");

            migrationBuilder.CreateIndex(
                name: "ix_member_entry_rules_member_id",
                schema: "attendance",
                table: "member_entry_rules",
                column: "member_id",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_member_measurements_gym_id",
                schema: "members",
                table: "member_measurements",
                column: "gym_id");

            migrationBuilder.CreateIndex(
                name: "ix_member_measurements_member_id_measured_on",
                schema: "members",
                table: "member_measurements",
                columns: new[] { "member_id", "measured_on" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "email_change_requests",
                schema: "identity");

            migrationBuilder.DropTable(
                name: "member_entry_rules",
                schema: "attendance");

            migrationBuilder.DropTable(
                name: "member_measurements",
                schema: "members");

            migrationBuilder.DropIndex(
                name: "ix_attendances_member_id_attendance_date",
                schema: "attendance",
                table: "attendances");

            migrationBuilder.DropColumn(
                name: "ends_on",
                schema: "classes",
                table: "gym_classes");

            migrationBuilder.CreateIndex(
                name: "ix_attendances_member_id_attendance_date",
                schema: "attendance",
                table: "attendances",
                columns: new[] { "member_id", "attendance_date" },
                unique: true);
        }
    }
}
