using GSM.Domain.Memberships;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace GSM.Infrastructure.Memberships;

/// <summary>MS7: una venta de un plan de grupo (Memberships v2, §9). Las membresías de las personas apuntan aquí con <c>group_id</c>.</summary>
public sealed class MembershipGroupConfiguration : IEntityTypeConfiguration<MembershipGroup>
{
    public void Configure(EntityTypeBuilder<MembershipGroup> builder)
    {
        builder.ToTable("membership_groups", "memberships", t =>
            t.HasCheckConstraint("ck_membership_groups_seats", "seats >= 2"));

        builder.HasIndex(g => g.HolderMemberId);
        builder.HasIndex(g => g.PlanId);
    }
}
