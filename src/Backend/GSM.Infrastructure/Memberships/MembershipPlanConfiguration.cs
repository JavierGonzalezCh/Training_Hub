using GSM.Domain.Memberships;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace GSM.Infrastructure.Memberships;

public sealed class MembershipPlanConfiguration : IEntityTypeConfiguration<MembershipPlan>
{
    public void Configure(EntityTypeBuilder<MembershipPlan> builder)
    {
        builder.ToTable("membership_plans", "memberships");

        builder.Property(p => p.Name).HasMaxLength(100).IsRequired();
        builder.Property(p => p.Description).HasMaxLength(500);
        builder.Property(p => p.Price).HasColumnType("decimal(12,2)");

        // MS3: la duración se expresa en días o en meses del calendario. La columna antigua
        // duration_days se renombra a duration_value (los planes existentes quedan en 'days').
        builder.Property(p => p.DurationValue).HasColumnName("duration_value");
        builder.Property(p => p.DurationUnit).HasColumnName("duration_unit").HasDefaultValue(DurationUnit.Days);
        builder.Property(p => p.IsActive).HasDefaultValue(true);

        builder.HasIndex(p => new { p.GymId, p.Name }).IsUnique();
    }
}
