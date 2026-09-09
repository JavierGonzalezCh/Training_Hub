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

        builder.HasIndex(s => s.GymId).IsUnique();
        builder.HasIndex(s => s.PaidThrough);
    }
}
