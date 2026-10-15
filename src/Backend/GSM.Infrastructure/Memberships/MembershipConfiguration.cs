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
        builder.Property(m => m.CancellationReason).HasMaxLength(500);
        builder.Property(m => m.RenewalKind).HasColumnName("renewal_kind");
        builder.Property(m => m.StartOverrideReason).HasMaxLength(500);

        // MS2: precio acordado, descuento con motivo y matrícula (COP).
        builder.Property(m => m.AgreedPrice).HasColumnType("decimal(12,2)");
        builder.Property(m => m.DiscountReason).HasMaxLength(500);
        builder.Property(m => m.EnrollmentFee).HasColumnType("decimal(12,2)");
        builder.Property(m => m.EnrollmentWaivedReason).HasMaxLength(500);
        builder.Ignore(m => m.TotalToCharge);

        // Índice requerido por el plan para el dashboard y la renovación.
        builder.HasIndex(m => new { m.MemberId, m.EndDate });
        builder.HasIndex(m => new { m.GymId, m.Status, m.EndDate });
        builder.HasIndex(m => m.PlanId);
    }
}
