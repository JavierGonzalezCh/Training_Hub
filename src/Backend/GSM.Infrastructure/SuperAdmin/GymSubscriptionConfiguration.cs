using GSM.Domain.SuperAdmin;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace GSM.Infrastructure.SuperAdmin;

public sealed class GymSubscriptionConfiguration : IEntityTypeConfiguration<GymSubscription>
{
    public void Configure(EntityTypeBuilder<GymSubscription> builder)
    {
        builder.ToTable("gym_subscriptions", "superadmin");

        builder.Property(s => s.Plan).HasColumnName("plan");
        builder.Property(s => s.MonthlyPrice).HasColumnType("decimal(12,2)");
        builder.Property(s => s.Notes).HasMaxLength(1000);

        // S1b: 3 por defecto para las filas existentes; ValueGeneratedNever para que un 0 explícito
        // ("sin gracia") no se confunda con "sin valor" y la base lo cambie a 3.
        builder.Property(s => s.TrialGraceDays)
            .HasDefaultValue(GymSubscription.DefaultTrialGraceDays)
            .ValueGeneratedNever();

        // S2: mismo schema, así que sí hay FK. Un plan en uso no se borra (se desactiva).
        builder.HasOne<SaasPlan>()
            .WithMany()
            .HasForeignKey(s => s.SaasPlanId)
            .HasConstraintName("fk_gym_subscriptions_saas_plans")
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasIndex(s => s.GymId).IsUnique();
        builder.HasIndex(s => s.PaidThrough);
        builder.HasIndex(s => s.SaasPlanId);
    }
}
