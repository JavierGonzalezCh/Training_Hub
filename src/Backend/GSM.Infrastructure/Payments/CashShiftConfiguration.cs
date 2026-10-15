using GSM.Domain.Payments;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace GSM.Infrastructure.Payments;

public sealed class CashShiftConfiguration : IEntityTypeConfiguration<CashShift>
{
    public void Configure(EntityTypeBuilder<CashShift> builder)
    {
        builder.ToTable("cash_shifts", "payments");

        builder.Property(s => s.OpeningCash).HasColumnType("decimal(12,2)");
        builder.Property(s => s.CountedCash).HasColumnType("decimal(12,2)");
        builder.Property(s => s.ExpectedCash).HasColumnType("decimal(12,2)");
        builder.Property(s => s.Difference).HasColumnType("decimal(12,2)");
        builder.Property(s => s.CloseNotes).HasMaxLength(CashShift.NotesMaxLength);
        builder.Ignore(s => s.IsOpen);

        // Una caja abierta por gym a la vez: dos aperturas simultáneas chocan aquí y no en el caso de uso.
        builder.HasIndex(s => s.GymId)
            .IsUnique()
            .HasFilter("closed_at IS NULL")
            .HasDatabaseName("ux_cash_shifts_open_gym");
        builder.HasIndex(s => new { s.GymId, s.OpenedAt });
        builder.HasIndex(s => s.OpenedBy);
    }
}

public sealed class CashMovementConfiguration : IEntityTypeConfiguration<CashMovement>
{
    public void Configure(EntityTypeBuilder<CashMovement> builder)
    {
        builder.ToTable("cash_movements", "payments");

        builder.Property(m => m.Type).HasColumnName("type");
        builder.Property(m => m.Amount).HasColumnType("decimal(12,2)");
        builder.Property(m => m.Reason).HasMaxLength(CashMovement.ReasonMaxLength).IsRequired();

        // Mismo schema, mismo módulo: la FK al turno sí cabe. Un turno no se borra.
        builder.HasOne<CashShift>()
            .WithMany()
            .HasForeignKey(m => m.ShiftId)
            .HasConstraintName("fk_cash_movements_cash_shifts")
            .OnDelete(DeleteBehavior.Restrict);
        builder.HasIndex(m => m.ShiftId);
        builder.HasIndex(m => new { m.GymId, m.OccurredAt });
    }
}
