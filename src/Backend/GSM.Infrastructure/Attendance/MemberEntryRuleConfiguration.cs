using GSM.Domain.Attendance;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace GSM.Infrastructure.Attendance;

public sealed class MemberEntryRuleConfiguration : IEntityTypeConfiguration<MemberEntryRule>
{
    public void Configure(EntityTypeBuilder<MemberEntryRule> builder)
    {
        builder.ToTable("member_entry_rules", "attendance", t =>
            t.HasCheckConstraint("ck_member_entry_rules_max_daily_entries", "max_daily_entries BETWEEN 2 AND 5"));

        builder.Property(r => r.Reason).HasMaxLength(500).IsRequired();

        // Una regla por miembro.
        builder.HasIndex(r => r.MemberId).IsUnique();
        builder.HasIndex(r => r.GymId);
    }
}
