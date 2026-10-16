using GSM.Domain.Gyms;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace GSM.Infrastructure.Gyms;

public sealed class GymFeatureFlagsConfiguration : IEntityTypeConfiguration<GymFeatureFlags>
{
    public void Configure(EntityTypeBuilder<GymFeatureFlags> builder)
    {
        builder.ToTable("gym_feature_flags", "gyms");

        builder.HasIndex(f => f.GymId).IsUnique();

        builder.HasOne<Gym>()
            .WithOne()
            .HasForeignKey<GymFeatureFlags>(f => f.GymId)
            .OnDelete(DeleteBehavior.Cascade);

        foreach (var flag in new[]
        {
            nameof(GymFeatureFlags.ShowRoutines), nameof(GymFeatureFlags.ShowClasses),
            nameof(GymFeatureFlags.ShowProgress), nameof(GymFeatureFlags.ShowGamification),
            nameof(GymFeatureFlags.ShowCommunity), nameof(GymFeatureFlags.CheckinQr),
        })
        {
            builder.Property(flag).HasDefaultValue(true);
        }

        // El auto-servicio arranca apagado: cada gym lo enciende a propósito.
        builder.Property(f => f.MembersCanCreateRoutines).HasDefaultValue(false);

        // Reportar fallas de máquinas está permitido por defecto (Equipment E4); los gyms existentes lo heredan encendido.
        builder.Property(f => f.MembersCanReportEquipment).HasDefaultValue(true);

        // El ranking voluntario arranca encendido (Achievements L6); los gyms existentes lo heredan encendido.
        builder.Property(f => f.ShowRanking).HasDefaultValue(true);
    }
}
