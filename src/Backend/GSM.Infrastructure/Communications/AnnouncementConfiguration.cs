using GSM.Domain.Communications;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace GSM.Infrastructure.Communications;

public sealed class AnnouncementConfiguration : IEntityTypeConfiguration<Announcement>
{
    public void Configure(EntityTypeBuilder<Announcement> builder)
    {
        builder.ToTable("announcements", "communications");

        builder.Property(a => a.Title).HasMaxLength(Announcement.TitleMaxLength).IsRequired();
        builder.Property(a => a.Body).HasMaxLength(Announcement.BodyMaxLength).IsRequired();
        builder.Property(a => a.Status).HasColumnName("status");

        builder.HasIndex(a => new { a.GymId, a.Status, a.PublishedAt });
    }
}
