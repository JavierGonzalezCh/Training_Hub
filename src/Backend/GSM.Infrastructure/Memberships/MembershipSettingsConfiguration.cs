using GSM.Domain.Memberships;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace GSM.Infrastructure.Memberships;

public sealed class MembershipSettingsConfiguration : IEntityTypeConfiguration<MembershipSettings>
{
    public void Configure(EntityTypeBuilder<MembershipSettings> builder)
    {
        builder.ToTable("membership_settings", "memberships");

        builder.HasIndex(s => s.GymId).IsUnique();
    }
}
