using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace GSM.Infrastructure.Migrations
{
    /// <inheritdoc />
    // IRREVERSIBLE EN DATOS: el Down recrea la columna members.qr_code, pero con valores nuevos.
    // Los QR personales que existian antes del Up se pierden (el QR personal fue retirado, etapa 12).
    public partial class Etapa12_3_DropMemberQrCode : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "ix_members_gym_id_qr_code",
                schema: "members",
                table: "members");

            migrationBuilder.DropColumn(
                name: "qr_code",
                schema: "members",
                table: "members");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "qr_code",
                schema: "members",
                table: "members",
                type: "character varying(64)",
                maxLength: 64,
                nullable: false,
                defaultValue: "");

            migrationBuilder.Sql(
                "update members.members set qr_code = replace(gen_random_uuid()::text, '-', '');");

            migrationBuilder.CreateIndex(
                name: "ix_members_gym_id_qr_code",
                schema: "members",
                table: "members",
                columns: new[] { "gym_id", "qr_code" },
                unique: true);
        }
    }
}
