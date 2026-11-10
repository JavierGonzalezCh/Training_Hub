using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace GSM.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class V2_RT1_TemplateImageAndBase : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "base_key",
                schema: "routines",
                table: "routine_templates",
                type: "character varying(60)",
                maxLength: 60,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "image_url",
                schema: "routines",
                table: "routine_templates",
                type: "character varying(500)",
                maxLength: 500,
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "ix_routine_templates_gym_id_base_key",
                schema: "routines",
                table: "routine_templates",
                columns: new[] { "gym_id", "base_key" },
                unique: true,
                filter: "base_key is not null");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "ix_routine_templates_gym_id_base_key",
                schema: "routines",
                table: "routine_templates");

            migrationBuilder.DropColumn(
                name: "base_key",
                schema: "routines",
                table: "routine_templates");

            migrationBuilder.DropColumn(
                name: "image_url",
                schema: "routines",
                table: "routine_templates");
        }
    }
}
