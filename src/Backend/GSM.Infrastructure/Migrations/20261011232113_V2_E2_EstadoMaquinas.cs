using System;
using GSM.Domain.Equipment;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace GSM.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class V2_E2_EstadoMaquinas : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "equipment_status_changes",
                schema: "equipment",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    gym_id = table.Column<Guid>(type: "uuid", nullable: false),
                    item_id = table.Column<Guid>(type: "uuid", nullable: false),
                    from_status = table.Column<EquipmentStatus>(type: "equipment.equipment_status", nullable: false),
                    to_status = table.Column<EquipmentStatus>(type: "equipment.equipment_status", nullable: false),
                    reason = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    expected_back_on = table.Column<DateOnly>(type: "date", nullable: true),
                    changed_by_user_id = table.Column<Guid>(type: "uuid", nullable: true),
                    changed_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_equipment_status_changes", x => x.id);
                    table.ForeignKey(
                        name: "fk_equipment_status_changes_equipment_items",
                        column: x => x.item_id,
                        principalSchema: "equipment",
                        principalTable: "equipment_items",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "ix_equipment_status_changes_item_id_changed_at",
                schema: "equipment",
                table: "equipment_status_changes",
                columns: new[] { "item_id", "changed_at" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "equipment_status_changes",
                schema: "equipment");
        }
    }
}
