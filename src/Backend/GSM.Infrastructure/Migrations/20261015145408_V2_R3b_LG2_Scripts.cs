using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace GSM.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class V2_R3b_LG2_Scripts : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Va aparte de V2_R3b_N1b_S1b: el valor 'weight_distance' no se puede usar en la misma
            // transacción en que se agrega al enum.
            migrationBuilder.SqlScript("R3b");
            migrationBuilder.SqlScript("LG2");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(
                "DROP TABLE IF EXISTS logs.business_rejections, logs.security_events, logs.slow_requests CASCADE;");
        }
    }
}
