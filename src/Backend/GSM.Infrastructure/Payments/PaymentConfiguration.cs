using GSM.Domain.Payments;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace GSM.Infrastructure.Payments;

public sealed class PaymentConfiguration : IEntityTypeConfiguration<Payment>
{
    public void Configure(EntityTypeBuilder<Payment> builder)
    {
        builder.ToTable("payments", "payments");

        builder.Property(p => p.Amount).HasColumnType("decimal(12,2)");
        builder.Property(p => p.Method).HasColumnName("method");
        builder.Property(p => p.Concept).HasMaxLength(200).IsRequired();
        builder.Property(p => p.Notes).HasMaxLength(1000);

        builder.HasIndex(p => new { p.GymId, p.PaidAt });
        builder.HasIndex(p => p.MemberId);
        builder.HasIndex(p => p.MembershipId);

        // P3: ligado al turno de caja (misma schema, sin FK dura: un pago antiguo no tiene turno).
        builder.HasIndex(p => p.ShiftId);
        // Sin HasDefaultValue(true): con un default en el modelo EF trata false como "sin valor" y lo reemplaza por
        // true al insertar. Las filas existentes se corrigen en V2_P3.sql.

        // Un pago se anula una sola vez: dos anulaciones simultáneas chocan aquí y no en la caja.
        builder.HasIndex(p => p.ReversesPaymentId).IsUnique();
    }
}
