using GSM.Domain.Achievements;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace GSM.Infrastructure.Achievements;

public sealed class AchievementConfiguration : IEntityTypeConfiguration<Achievement>
{
    public void Configure(EntityTypeBuilder<Achievement> builder)
    {
        builder.ToTable("achievements", "achievements");

        builder.Property(a => a.Name).HasMaxLength(100).IsRequired();
        builder.Property(a => a.Description).HasMaxLength(500);
        builder.Property(a => a.IconUrl).HasMaxLength(2048);
        builder.Property(a => a.ConditionType).HasColumnName("condition_type");
        builder.Property(a => a.IsActive).HasDefaultValue(true);

        builder.HasIndex(a => new { a.GymId, a.Name }).IsUnique();
    }
}

public sealed class MemberAchievementConfiguration : IEntityTypeConfiguration<MemberAchievement>
{
    public void Configure(EntityTypeBuilder<MemberAchievement> builder)
    {
        builder.ToTable("member_achievements", "achievements");

        builder.HasIndex(m => new { m.MemberId, m.AchievementId }).IsUnique();
        builder.HasIndex(m => m.AchievementId);

        builder.HasOne<Achievement>()
            .WithMany()
            .HasForeignKey(m => m.AchievementId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}
