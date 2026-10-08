using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace GSM.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class V2_S7_MonthlyMetrics : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "monthly_metrics",
                schema: "superadmin",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    month = table.Column<DateOnly>(type: "date", nullable: false),
                    mrr = table.Column<decimal>(type: "numeric(14,2)", nullable: false),
                    collected = table.Column<decimal>(type: "numeric(14,2)", nullable: false),
                    new_gyms = table.Column<int>(type: "integer", nullable: false),
                    trial_gyms_at_close = table.Column<int>(type: "integer", nullable: false),
                    converted_gyms = table.Column<int>(type: "integer", nullable: false),
                    lost_gyms = table.Column<int>(type: "integer", nullable: false),
                    active_gyms_at_start = table.Column<int>(type: "integer", nullable: false),
                    active_gyms_at_close = table.Column<int>(type: "integer", nullable: false),
                    churn = table.Column<decimal>(type: "numeric(7,4)", nullable: true),
                    active_members = table.Column<int>(type: "integer", nullable: false),
                    check_ins = table.Column<int>(type: "integer", nullable: false),
                    gym_states = table.Column<string>(type: "jsonb", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_monthly_metrics", x => x.id);
                });

            migrationBuilder.CreateIndex(
                name: "ix_monthly_metrics_month",
                schema: "superadmin",
                table: "monthly_metrics",
                column: "month",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "monthly_metrics",
                schema: "superadmin");
        }
    }
}
