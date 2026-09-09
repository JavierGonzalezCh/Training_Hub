using GSM.Domain.Achievements;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace GSM.Infrastructure.Achievements;

public sealed class StreakFreezeConfiguration : IEntityTypeConfiguration<StreakFreeze>
{
    public void Configure(EntityTypeBuilder<StreakFreeze> builder)
    {
        builder.ToTable("streak_freezes", "achievements");

        builder.Property(f => f.GrantedOn).HasColumnType("date");
        builder.Property(f => f.UsedOn).HasColumnType("date");

        // Saldo y concesión perezosa parten del socio, ordenando por granted_on.
        builder.HasIndex(f => new { f.MemberId, f.GrantedOn });

        // Único parcial (member_id, used_on) filtrado a los consumidos: un solo congelador por
        // fecha fallada → consumo idempotente. También sirve las búsquedas por fecha gastada.
        builder.HasIndex(f => new { f.MemberId, f.UsedOn })
            .IsUnique()
            .HasFilter("used_on IS NOT NULL")
            .HasDatabaseName("ix_streak_freezes_member_id_used_on_spent");
    }
}
