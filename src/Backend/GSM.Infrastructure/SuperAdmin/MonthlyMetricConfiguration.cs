using System.Text.Json;
using GSM.Domain.SuperAdmin;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;
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
        // Texto JSON en una columna jsonb: evita el JSON dinamico de Npgsql, que exige un opt-in global.
        builder.Property(m => m.GymStates)
            .HasColumnType("jsonb")
            .HasConversion(
                v => JsonSerializer.Serialize(v, (JsonSerializerOptions?)null),
                v => JsonSerializer.Deserialize<Dictionary<string, string>>(v, (JsonSerializerOptions?)null)
                     ?? new Dictionary<string, string>(),
                new ValueComparer<Dictionary<string, string>>(
                    (a, b) => a != null && b != null && a.Count == b.Count && !a.Except(b).Any(),
                    v => v.Aggregate(0, (h, kv) => HashCode.Combine(h, kv.Key, kv.Value)),
                    v => new Dictionary<string, string>(v)));

        builder.HasIndex(m => m.Month).IsUnique();
    }
}
