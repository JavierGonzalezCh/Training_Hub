using GSM.Domain.Classes;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace GSM.Infrastructure.Classes;

public sealed class ClassBookingConfiguration : IEntityTypeConfiguration<ClassBooking>
{
    public void Configure(EntityTypeBuilder<ClassBooking> builder)
    {
        builder.ToTable("class_bookings", "classes");

        builder.Property(b => b.Status)
            .HasColumnName("status")
            .HasColumnType("classes.class_booking_status");

        builder.HasOne<ClassSession>()
            .WithMany()
            .HasForeignKey(b => b.SessionId)
            .OnDelete(DeleteBehavior.Cascade);

        // Una fila por miembro y sesión; reservar de nuevo tras cancelar reactiva esta misma fila.
        builder.HasIndex(b => new { b.SessionId, b.MemberId }).IsUnique();
        builder.HasIndex(b => b.MemberId);
    }
}
