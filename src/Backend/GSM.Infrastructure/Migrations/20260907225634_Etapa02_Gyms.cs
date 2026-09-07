using System;
using GSM.Domain.Gyms;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace GSM.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class Etapa02_Gyms : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.EnsureSchema(
                name: "gyms");

            migrationBuilder.AlterDatabase()
                .Annotation("Npgsql:Enum:gyms.gym_plan", "basic,pro,enterprise")
                .Annotation("Npgsql:Enum:gyms.gym_status", "trial,active,suspended")
                .Annotation("Npgsql:Enum:identity.user_role", "superadmin,owner,staff,member")
                .OldAnnotation("Npgsql:Enum:identity.user_role", "superadmin,owner,staff,member");

            migrationBuilder.CreateTable(
                name: "gyms",
                schema: "gyms",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    name = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    slug = table.Column<string>(type: "character varying(60)", maxLength: 60, nullable: false),
                    nit = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: true),
                    phone = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: true),
                    email = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: true),
                    logo_url = table.Column<string>(type: "character varying(2048)", maxLength: 2048, nullable: true),
                    primary_color = table.Column<string>(type: "character varying(7)", maxLength: 7, nullable: false),
                    secondary_color = table.Column<string>(type: "character varying(7)", maxLength: 7, nullable: false),
                    is_active = table.Column<bool>(type: "boolean", nullable: false),
                    plan = table.Column<GymPlan>(type: "gyms.gym_plan", nullable: false),
                    status = table.Column<GymStatus>(type: "gyms.gym_status", nullable: false),
                    owner_id = table.Column<Guid>(type: "uuid", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_gyms", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "gym_feature_flags",
                schema: "gyms",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    gym_id = table.Column<Guid>(type: "uuid", nullable: false),
                    show_routines = table.Column<bool>(type: "boolean", nullable: false, defaultValue: true),
                    show_classes = table.Column<bool>(type: "boolean", nullable: false, defaultValue: true),
                    show_progress = table.Column<bool>(type: "boolean", nullable: false, defaultValue: true),
                    show_gamification = table.Column<bool>(type: "boolean", nullable: false, defaultValue: true),
                    show_community = table.Column<bool>(type: "boolean", nullable: false, defaultValue: true),
                    checkin_qr = table.Column<bool>(type: "boolean", nullable: false, defaultValue: true),
                    community_comments = table.Column<bool>(type: "boolean", nullable: false, defaultValue: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_gym_feature_flags", x => x.id);
                    table.ForeignKey(
                        name: "fk_gym_feature_flags_gyms_gym_id",
                        column: x => x.gym_id,
                        principalSchema: "gyms",
                        principalTable: "gyms",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "ix_gym_feature_flags_gym_id",
                schema: "gyms",
                table: "gym_feature_flags",
                column: "gym_id",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_gyms_owner_id",
                schema: "gyms",
                table: "gyms",
                column: "owner_id");

            migrationBuilder.CreateIndex(
                name: "ix_gyms_slug",
                schema: "gyms",
                table: "gyms",
                column: "slug",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "gym_feature_flags",
                schema: "gyms");

            migrationBuilder.DropTable(
                name: "gyms",
                schema: "gyms");

            migrationBuilder.AlterDatabase()
                .Annotation("Npgsql:Enum:identity.user_role", "superadmin,owner,staff,member")
                .OldAnnotation("Npgsql:Enum:gyms.gym_plan", "basic,pro,enterprise")
                .OldAnnotation("Npgsql:Enum:gyms.gym_status", "trial,active,suspended")
                .OldAnnotation("Npgsql:Enum:identity.user_role", "superadmin,owner,staff,member");
        }
    }
}
