using GSM.Domain.Memberships;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace GSM.Infrastructure.Memberships;

public sealed class MembershipConfiguration : IEntityTypeConfiguration<Membership>
{
    public void Configure(EntityTypeBuilder<Membership> builder)
    {
        builder.ToTable("memberships", "memberships", t =>
        {
            t.HasCheckConstraint(
                "ck_memberships_credit_applied", "credit_applied >= 0 AND credit_applied <= agreed_price + enrollment_fee");
            t.HasCheckConstraint("ck_memberships_group_holder", "NOT is_group_holder OR group_id IS NOT NULL");
        });

        // MS7: planes de grupo. Cada persona del grupo tiene su membresía con el mismo group_id; el titular lleva el precio.
        builder.Property(m => m.GroupId).HasColumnName("group_id");
        builder.Property(m => m.IsGroupHolder).HasColumnName("is_group_holder").HasDefaultValue(false);
        builder.HasIndex(m => m.GroupId);
        builder.HasOne<MembershipGroup>()
            .WithMany()
            .HasForeignKey(m => m.GroupId)
            .OnDelete(DeleteBehavior.Restrict);

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

        // MS6: cambio de plan con crédito. El crédito nunca es negativo ni supera lo que cuesta la membresía.
        builder.Property(m => m.CreditApplied).HasColumnType("decimal(12,2)").HasDefaultValue(0m);
        builder.Property(m => m.CreditAdjustmentReason).HasMaxLength(500);
        builder.Property(m => m.ReplacesMembershipId).HasColumnName("replaces_membership_id");
        builder.HasIndex(m => m.ReplacesMembershipId);

        // MS3: día del mes en que arranca el ciclo, solo en planes en meses del calendario.
        builder.Property(m => m.CycleDay).HasColumnName("cycle_day");

        // MS5: entradas con que empezó una tiquetera (nulo en una membresía por tiempo). Las usadas viven en pass_usages.
        builder.Property(m => m.EntriesTotal).HasColumnName("entries_total");
        builder.Ignore(m => m.IsPass);

        // L2: días congelados en pausas ya cerradas; la antigüedad no los cuenta.

        // Índice requerido por el plan para el dashboard y la renovación.
        builder.HasIndex(m => new { m.MemberId, m.EndDate });
        builder.HasIndex(m => new { m.GymId, m.Status, m.EndDate });
        builder.HasIndex(m => m.PlanId);
    }
}
