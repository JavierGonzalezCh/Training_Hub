using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace GSM.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class V2_LG1_Logs : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Schema `logs` completo (tablas, particiones de `errors`): no es parte del modelo de GsmDbContext,
            // lo mapea LogsDbContext. Todo el SQL vive en Scripts/V2_LG1.sql.
            migrationBuilder.SqlScript("LG1");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("DROP SCHEMA IF EXISTS logs CASCADE;");
        }
    }
}
