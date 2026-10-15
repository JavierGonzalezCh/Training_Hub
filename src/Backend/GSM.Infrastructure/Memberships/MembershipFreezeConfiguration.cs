using GSM.Domain.Memberships;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace GSM.Infrastructure.Memberships;

/// <summary>MS4: congelamientos de una membresía (Memberships v2, §5.2). Historial: no se borran al terminar.</summary>
public sealed class MembershipFreezeConfiguration : IEntityTypeConfiguration<MembershipFreeze>
{
    public void Configure(EntityTypeBuilder<MembershipFreeze> builder)
    {
        builder.ToTable("membership_freezes", "memberships", t =>
        {
            t.HasCheckConstraint("ck_membership_freezes_range", "ends_on IS NULL OR ends_on >= starts_on");
            t.HasCheckConstraint("ck_membership_freezes_days_credited", "days_credited >= 0");
        });

        builder.Property(f => f.Reason).HasMaxLength(MembershipFreeze.MaxReasonLength).IsRequired();
        builder.Property(f => f.OverrideLimit).HasDefaultValue(false);
        builder.Property(f => f.DaysCredited).HasDefaultValue(0);
        builder.Ignore(f => f.IsOpen);
        builder.Ignore(f => f.Length);

        builder.HasIndex(f => f.MembershipId);
        builder.HasIndex(f => new { f.GymId, f.StartsOn });

        builder.HasOne<Membership>()
            .WithMany()
            .HasForeignKey(f => f.MembershipId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}
