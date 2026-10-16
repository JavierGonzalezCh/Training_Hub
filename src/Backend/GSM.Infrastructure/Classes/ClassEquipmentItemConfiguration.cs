using GSM.Domain.Classes;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace GSM.Infrastructure.Classes;

public sealed class ClassEquipmentItemConfiguration : IEntityTypeConfiguration<ClassEquipmentItem>
{
    public void Configure(EntityTypeBuilder<ClassEquipmentItem> builder)
    {
        builder.ToTable("class_equipment_items", "classes");

        // Sin FK a equipment.*: no hay FK entre schemas de módulos distintos.
        builder.Property(e => e.EquipmentItemId).HasColumnName("equipment_item_id");

        builder.HasOne<GymClass>()
            .WithMany()
            .HasForeignKey(e => e.ClassId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasIndex(e => new { e.ClassId, e.EquipmentItemId })
            .IsUnique()
            .HasDatabaseName("ix_class_equipment_items_class_item");
        builder.HasIndex(e => e.EquipmentItemId);
    }
}
