using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace GSM.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class V1_1_PaymentReversals : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "reverses_payment_id",
                schema: "payments",
                table: "payments",
                type: "uuid",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "ix_payments_reverses_payment_id",
                schema: "payments",
                table: "payments",
                column: "reverses_payment_id",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "ix_payments_reverses_payment_id",
                schema: "payments",
                table: "payments");

            migrationBuilder.DropColumn(
                name: "reverses_payment_id",
                schema: "payments",
                table: "payments");
        }
    }
}
