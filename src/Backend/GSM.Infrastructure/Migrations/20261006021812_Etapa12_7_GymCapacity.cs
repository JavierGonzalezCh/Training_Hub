using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace GSM.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class Etapa12_7_GymCapacity : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "capacity",
                schema: "gyms",
                table: "gyms",
                type: "integer",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "capacity",
                schema: "gyms",
                table: "gyms");
        }
    }
}
