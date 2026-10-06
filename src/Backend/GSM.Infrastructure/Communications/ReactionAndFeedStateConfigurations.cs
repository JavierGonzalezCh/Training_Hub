using GSM.Domain.Communications;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace GSM.Infrastructure.Communications;

public sealed class AnnouncementReactionConfiguration : IEntityTypeConfiguration<AnnouncementReaction>
{
    public void Configure(EntityTypeBuilder<AnnouncementReaction> builder)
    {
        builder.ToTable("announcement_reactions", "communications");

        builder.Property(r => r.Kind).HasColumnName("kind");

        // Una reacción por miembro y anuncio; también sirve para agregar por anuncio.
        builder.HasIndex(r => new { r.AnnouncementId, r.MemberId }).IsUnique();

        builder.HasOne<Announcement>()
            .WithMany()
            .HasForeignKey(r => r.AnnouncementId)
            .HasConstraintName("fk_announcement_reactions_announcements")
            .OnDelete(DeleteBehavior.Cascade);
    }
}

public sealed class MemberFeedStateConfiguration : IEntityTypeConfiguration<MemberFeedState>
{
    public void Configure(EntityTypeBuilder<MemberFeedState> builder)
    {
        builder.ToTable("member_feed_states", "communications");

        builder.HasIndex(s => s.MemberId).IsUnique();
    }
}
