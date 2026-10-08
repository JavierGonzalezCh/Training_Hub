using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace GSM.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class V2_P1_PaymentMethods : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Solo cambia un enum nativo: el esquema de tablas no varía. El SQL (renombrar transfer ->
            // bank_transfer y añadir los valores nuevos) está a mano en Scripts/V2_P1.sql.
            migrationBuilder.SqlScript("P1");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // ADD VALUE no se revierte en Postgres; solo se devuelve el nombre del valor renombrado.
            migrationBuilder.Sql("ALTER TYPE payments.payment_method RENAME VALUE 'bank_transfer' TO 'transfer';");
        }
    }
}
