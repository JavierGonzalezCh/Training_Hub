using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace GSM.Infrastructure.Migrations
{
    /// <summary>
    /// <c>class_booking_status</c> persistía <c>noshow</c> porque el enum se registró con
    /// <c>LowerCaseNameTranslator</c>; la especificación de Classes pide <c>no_show</c>.
    /// </summary>
    /// <remarks>
    /// Escrita a mano: EF solo genera un cambio de anotación, y el generador de Npgsql no sabe
    /// expresar un renombrado de etiqueta (solo añade valores). <c>RENAME VALUE</c> renombra la
    /// etiqueta en el tipo y las filas existentes la siguen solas; a diferencia de <c>ADD VALUE</c>,
    /// sí se revierte. El Designer y el snapshot ya registran el modelo con <c>no_show</c>.
    /// </remarks>
    public partial class Etapa10_5_BookingStatusNoShow : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
            => migrationBuilder.Sql("ALTER TYPE classes.class_booking_status RENAME VALUE 'noshow' TO 'no_show';");

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
            => migrationBuilder.Sql("ALTER TYPE classes.class_booking_status RENAME VALUE 'no_show' TO 'noshow';");
    }
}
