using GSM.Domain.SuperAdmin;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace GSM.Infrastructure.SuperAdmin;

public sealed class SaasPlanConfiguration : IEntityTypeConfiguration<SaasPlan>
{
    public void Configure(EntityTypeBuilder<SaasPlan> builder)
    {
        builder.ToTable("saas_plans", "superadmin");

        builder.Property(p => p.Name).HasMaxLength(SaasPlan.NameMaxLength).IsRequired();
        builder.Property(p => p.SuggestedPrice).HasColumnType("decimal(12,2)");

        builder.HasIndex(p => p.Name).IsUnique();
    }
}
