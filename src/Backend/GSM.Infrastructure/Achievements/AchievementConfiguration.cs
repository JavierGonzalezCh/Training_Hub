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
        builder.Property(a => a.RewardDescription).HasMaxLength(200);
        builder.Ignore(a => a.HasReward);

        builder.HasIndex(a => new { a.GymId, a.Name }).IsUnique();
    }
}

public sealed class MemberAchievementConfiguration : IEntityTypeConfiguration<MemberAchievement>
{
    public void Configure(EntityTypeBuilder<MemberAchievement> builder)
    {
        builder.ToTable("member_achievements", "achievements");

        builder.Property(m => m.GrantReason).HasMaxLength(300);
        builder.Property(m => m.RevokeReason).HasMaxLength(300);

        // L5: guardado como texto (none / pending / delivered); el vencido se calcula, nunca se persiste.
        builder.Property(m => m.RewardStatus)
            .HasColumnName("reward_status")
            .HasMaxLength(16)
            .HasDefaultValue(RewardStatus.None)
            .HasConversion(v => v.ToString().ToLowerInvariant(), v => Enum.Parse<RewardStatus>(v, true));
        builder.Property(m => m.RewardDescription).HasMaxLength(200);
        builder.HasIndex(m => new { m.GymId, m.RewardStatus }).HasDatabaseName("ix_member_achievements_gym_id_reward_status");

        builder.HasIndex(m => new { m.MemberId, m.AchievementId }).IsUnique();
        builder.HasIndex(m => m.AchievementId);

        builder.HasOne<Achievement>()
            .WithMany()
            .HasForeignKey(m => m.AchievementId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}
