using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace GSM.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class V2_G2_GymProfile : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "address",
                schema: "gyms",
                table: "gyms",
                type: "character varying(300)",
                maxLength: 300,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "city",
                schema: "gyms",
                table: "gyms",
                type: "character varying(100)",
                maxLength: 100,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "facebook_url",
                schema: "gyms",
                table: "gyms",
                type: "character varying(2048)",
                maxLength: 2048,
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "geofence_radius_meters",
                schema: "gyms",
                table: "gyms",
                type: "integer",
                nullable: false,
                defaultValue: 150);

            migrationBuilder.AddColumn<string>(
                name: "instagram_url",
                schema: "gyms",
                table: "gyms",
                type: "character varying(2048)",
                maxLength: 2048,
                nullable: true);

            migrationBuilder.AddColumn<double>(
                name: "latitude",
                schema: "gyms",
                table: "gyms",
                type: "double precision",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "legal_name",
                schema: "gyms",
                table: "gyms",
                type: "character varying(200)",
                maxLength: 200,
                nullable: true);

            migrationBuilder.AddColumn<double>(
                name: "longitude",
                schema: "gyms",
                table: "gyms",
                type: "double precision",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "tik_tok_url",
                schema: "gyms",
                table: "gyms",
                type: "character varying(2048)",
                maxLength: 2048,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "website_url",
                schema: "gyms",
                table: "gyms",
                type: "character varying(2048)",
                maxLength: 2048,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "whats_app_phone",
                schema: "gyms",
                table: "gyms",
                type: "character varying(20)",
                maxLength: 20,
                nullable: true);

            migrationBuilder.SqlScript("G2");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "address",
                schema: "gyms",
                table: "gyms");

            migrationBuilder.DropColumn(
                name: "city",
                schema: "gyms",
                table: "gyms");

            migrationBuilder.DropColumn(
                name: "facebook_url",
                schema: "gyms",
                table: "gyms");

            migrationBuilder.DropColumn(
                name: "geofence_radius_meters",
                schema: "gyms",
                table: "gyms");

            migrationBuilder.DropColumn(
                name: "instagram_url",
                schema: "gyms",
                table: "gyms");

            migrationBuilder.DropColumn(
                name: "latitude",
                schema: "gyms",
                table: "gyms");

            migrationBuilder.DropColumn(
                name: "legal_name",
                schema: "gyms",
                table: "gyms");

            migrationBuilder.DropColumn(
                name: "longitude",
                schema: "gyms",
                table: "gyms");

            migrationBuilder.DropColumn(
                name: "tik_tok_url",
                schema: "gyms",
                table: "gyms");

            migrationBuilder.DropColumn(
                name: "website_url",
                schema: "gyms",
                table: "gyms");

            migrationBuilder.DropColumn(
                name: "whats_app_phone",
                schema: "gyms",
                table: "gyms");
        }
    }
}
