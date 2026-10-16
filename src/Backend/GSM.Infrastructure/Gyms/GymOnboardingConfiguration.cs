using GSM.Domain.Gyms;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace GSM.Infrastructure.Gyms;

public sealed class GymOnboardingConfiguration : IEntityTypeConfiguration<GymOnboarding>
{
    public void Configure(EntityTypeBuilder<GymOnboarding> builder)
    {
        builder.ToTable("gym_onboarding", "gyms");

        builder.Property(o => o.SkipReason).HasMaxLength(GymOnboarding.MaxSkipReasonLength);
        builder.HasIndex(o => o.GymId).IsUnique();

        builder.HasOne<Gym>()
            .WithOne()
            .HasForeignKey<GymOnboarding>(o => o.GymId)
            .HasConstraintName("fk_gym_onboarding_gyms")
            .OnDelete(DeleteBehavior.Cascade);
    }
}
