using System;
using GSM.Domain.Payments;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace GSM.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class Etapa11_3_SubscriptionPayments : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "subscription_payments",
                schema: "superadmin",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    gym_id = table.Column<Guid>(type: "uuid", nullable: false),
                    amount = table.Column<decimal>(type: "numeric(12,2)", nullable: false),
                    method = table.Column<PaymentMethod>(type: "payments.payment_method", nullable: false),
                    covers_from = table.Column<DateOnly>(type: "date", nullable: false),
                    covers_through = table.Column<DateOnly>(type: "date", nullable: false),
                    paid_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    registered_by = table.Column<Guid>(type: "uuid", nullable: false),
                    reference = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    notes = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_subscription_payments", x => x.id);
                });

            migrationBuilder.CreateIndex(
                name: "ix_subscription_payments_gym_id_paid_at",
                schema: "superadmin",
                table: "subscription_payments",
                columns: new[] { "gym_id", "paid_at" });

            migrationBuilder.CreateIndex(
                name: "ix_subscription_payments_paid_at",
                schema: "superadmin",
                table: "subscription_payments",
                column: "paid_at");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "subscription_payments",
                schema: "superadmin");
        }
    }
}
