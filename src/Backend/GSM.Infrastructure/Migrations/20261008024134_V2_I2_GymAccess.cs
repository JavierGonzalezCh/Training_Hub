using System;
using GSM.Domain.Identity;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace GSM.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class V2_I2_GymAccess : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "is_super_admin",
                schema: "identity",
                table: "users",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<Guid>(
                name: "gym_access_id",
                schema: "identity",
                table: "refresh_tokens",
                type: "uuid",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "gym_accesses",
                schema: "identity",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    gym_id = table.Column<Guid>(type: "uuid", nullable: false),
                    role = table.Column<UserRole>(type: "identity.user_role", nullable: false),
                    is_team = table.Column<bool>(type: "boolean", nullable: false),
                    is_active = table.Column<bool>(type: "boolean", nullable: false, defaultValue: true),
                    deactivated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    last_used_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_gym_accesses", x => x.id);
                    table.ForeignKey(
                        name: "fk_gym_accesses_users_user_id",
                        column: x => x.user_id,
                        principalSchema: "identity",
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "ix_refresh_tokens_gym_access_id",
                schema: "identity",
                table: "refresh_tokens",
                column: "gym_access_id");

            migrationBuilder.CreateIndex(
                name: "ix_gym_accesses_gym_id_role",
                schema: "identity",
                table: "gym_accesses",
                columns: new[] { "gym_id", "role" });

            migrationBuilder.CreateIndex(
                name: "ix_gym_accesses_user_id_gym_id_is_team",
                schema: "identity",
                table: "gym_accesses",
                columns: new[] { "user_id", "gym_id", "is_team" },
                unique: true);

            // Copia cada cuenta actual a su GymAccess ANTES de borrar users.gym_id / users.role (V2_I2.sql).
            migrationBuilder.SqlScript("I2");

            migrationBuilder.DropIndex(
                name: "ix_users_gym_id",
                schema: "identity",
                table: "users");

            migrationBuilder.DropColumn(
                name: "gym_id",
                schema: "identity",
                table: "users");

            migrationBuilder.DropColumn(
                name: "role",
                schema: "identity",
                table: "users");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "gym_id",
                schema: "identity",
                table: "users",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<UserRole>(
                name: "role",
                schema: "identity",
                table: "users",
                type: "identity.user_role",
                nullable: false,
                defaultValue: UserRole.SuperAdmin);

            migrationBuilder.CreateIndex(
                name: "ix_users_gym_id",
                schema: "identity",
                table: "users",
                column: "gym_id");

            // Vuelve a un gym y un rol por cuenta (con pérdida si había varios accesos).
            migrationBuilder.SqlScript("I2_down");

            migrationBuilder.DropTable(
                name: "gym_accesses",
                schema: "identity");

            migrationBuilder.DropIndex(
                name: "ix_refresh_tokens_gym_access_id",
                schema: "identity",
                table: "refresh_tokens");

            migrationBuilder.DropColumn(
                name: "is_super_admin",
                schema: "identity",
                table: "users");

            migrationBuilder.DropColumn(
                name: "gym_access_id",
                schema: "identity",
                table: "refresh_tokens");
        }
    }
}
