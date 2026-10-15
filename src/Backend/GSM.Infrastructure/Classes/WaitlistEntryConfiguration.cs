using GSM.Domain.Classes;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace GSM.Infrastructure.Classes;

public sealed class WaitlistEntryConfiguration : IEntityTypeConfiguration<WaitlistEntry>
{
    public void Configure(EntityTypeBuilder<WaitlistEntry> builder)
    {
        builder.ToTable("waitlist_entries", "classes");

        builder.Property(w => w.Status)
            .HasColumnName("status")
            .HasColumnType("classes.waitlist_entry_status");

        builder.HasOne<ClassSession>()
            .WithMany()
            .HasForeignKey(w => w.SessionId)
            .OnDelete(DeleteBehavior.Cascade);

        // El orden de la lista: una posición por sesión. Dos anotaciones simultáneas chocan aquí y la segunda reintenta.
        builder.HasIndex(w => new { w.SessionId, w.Position })
            .IsUnique()
            .HasDatabaseName("ix_waitlist_entries_session_position");

        // Una sola entrada abierta por miembro y sesión (salir y volver a anotarse crea otra fila).
        builder.HasIndex(w => new { w.SessionId, w.MemberId })
            .IsUnique()
            .HasFilter("status IN ('waiting', 'offered')")
            .HasDatabaseName("ix_waitlist_entries_session_member_open");

        // Las listas abiertas de un miembro (máximo de reservas, "mi espera") y las ofertas por vencer (tarea periódica).
        builder.HasIndex(w => new { w.MemberId, w.Status });
        builder.HasIndex(w => w.OfferExpiresAt)
            .HasFilter("status = 'offered'")
            .HasDatabaseName("ix_waitlist_entries_offer_expires");
    }
}
