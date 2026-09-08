using GSM.Domain.Memberships;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace GSM.Infrastructure.Memberships;

public sealed class MembershipConfiguration : IEntityTypeConfiguration<Membership>
{
    public void Configure(EntityTypeBuilder<Membership> builder)
    {
        builder.ToTable("memberships", "memberships");

        builder.Property(m => m.Status).HasColumnName("status");

        // Índice requerido por el plan para el dashboard y la renovación.
        builder.HasIndex(m => new { m.MemberId, m.EndDate });
        builder.HasIndex(m => new { m.GymId, m.Status, m.EndDate });
        builder.HasIndex(m => m.PlanId);
    }
}
