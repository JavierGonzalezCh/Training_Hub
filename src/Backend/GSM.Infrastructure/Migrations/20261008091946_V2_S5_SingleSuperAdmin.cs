using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace GSM.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class V2_S5_SingleSuperAdmin : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Falla con un mensaje claro si ya hay mas de un SuperAdmin (no se elige cual se queda).
            migrationBuilder.SqlScript("S5");

            migrationBuilder.CreateIndex(
                name: "ux_users_single_super_admin",
                schema: "identity",
                table: "users",
                column: "is_super_admin",
                unique: true,
                filter: "is_super_admin");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "ux_users_single_super_admin",
                schema: "identity",
                table: "users");
        }
    }
}
