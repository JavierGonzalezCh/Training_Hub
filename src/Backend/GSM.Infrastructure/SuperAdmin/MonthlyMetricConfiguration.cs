using GSM.Domain.SuperAdmin;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace GSM.Infrastructure.SuperAdmin;

public sealed class MonthlyMetricConfiguration : IEntityTypeConfiguration<MonthlyMetric>
{
    public void Configure(EntityTypeBuilder<MonthlyMetric> builder)
    {
        builder.ToTable("monthly_metrics", "superadmin");

        builder.Property(m => m.Mrr).HasColumnType("decimal(14,2)");
        builder.Property(m => m.Collected).HasColumnType("decimal(14,2)");
        builder.Property(m => m.Churn).HasColumnType("decimal(7,4)");
        builder.Property(m => m.GymStates).HasColumnType("jsonb");

        builder.HasIndex(m => m.Month).IsUnique();
    }
}
