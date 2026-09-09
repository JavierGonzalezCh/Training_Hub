using System;
using GSM.Domain.Gyms;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace GSM.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class Etapa11_1_Subscriptions : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.EnsureSchema(
                name: "superadmin");

            migrationBuilder.CreateTable(
                name: "gym_subscriptions",
                schema: "superadmin",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    gym_id = table.Column<Guid>(type: "uuid", nullable: false),
                    plan = table.Column<GymPlan>(type: "gyms.gym_plan", nullable: false),
                    monthly_price = table.Column<decimal>(type: "numeric(12,2)", nullable: false),
                    started_on = table.Column<DateOnly>(type: "date", nullable: false),
                    trial_ends_on = table.Column<DateOnly>(type: "date", nullable: true),
                    paid_through = table.Column<DateOnly>(type: "date", nullable: false),
                    notes = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_gym_subscriptions", x => x.id);
                });

            migrationBuilder.CreateIndex(
                name: "ix_gym_subscriptions_gym_id",
                schema: "superadmin",
                table: "gym_subscriptions",
                column: "gym_id",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_gym_subscriptions_paid_through",
                schema: "superadmin",
                table: "gym_subscriptions",
                column: "paid_through");

            // Alta de suscripción para cada gym existente: sin esto los gyms ya creados quedan
            // sin suscripción. Estado inicial 'active' (paid_through a 30 días vista), precio 0
            // (aún no negociado), plan heredado del gym.
            migrationBuilder.Sql(
                """
                insert into superadmin.gym_subscriptions
                    (id, gym_id, plan, monthly_price, started_on, trial_ends_on, paid_through, notes, created_at, updated_at)
                select
                    gen_random_uuid(), g.id, g.plan, 0, current_date, null, current_date + 30, null, now(), now()
                from gyms.gyms g
                where not exists (
                    select 1 from superadmin.gym_subscriptions s where s.gym_id = g.id
                );
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "gym_subscriptions",
                schema: "superadmin");
        }
    }
}
