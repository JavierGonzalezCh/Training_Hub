using System.Text.Json;
using GSM.Domain.Communications;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;
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
        builder.Property(a => a.Category).HasColumnName("category");

        // CO2: los filtros de la audiencia viajan en una sola columna jsonb. El comparador mira el JSON
        // (los arreglos del record se comparan por referencia).
        var comparer = new ValueComparer<AnnouncementAudience>(
            (x, y) => JsonSerializer.Serialize(x, (JsonSerializerOptions?)null) == JsonSerializer.Serialize(y, (JsonSerializerOptions?)null),
            x => JsonSerializer.Serialize(x, (JsonSerializerOptions?)null).GetHashCode(),
            x => JsonSerializer.Deserialize<AnnouncementAudience>(JsonSerializer.Serialize(x, (JsonSerializerOptions?)null), (JsonSerializerOptions?)null)!);
        builder.Property(a => a.Audience)
            .HasColumnName("audience")
            .HasColumnType("jsonb")
            .HasConversion(
                v => JsonSerializer.Serialize(v, (JsonSerializerOptions?)null),
                v => JsonSerializer.Deserialize<AnnouncementAudience>(v, (JsonSerializerOptions?)null) ?? AnnouncementAudience.Everyone,
                comparer)
            .HasDefaultValueSql("'{}'::jsonb")
            .IsRequired();
        builder.Property(a => a.IsSegmented).HasDefaultValue(false);

        builder.HasIndex(a => new { a.GymId, a.Status, a.PublishedAt });
    }
}

public sealed class AnnouncementRecipientConfiguration : IEntityTypeConfiguration<AnnouncementRecipient>
{
    public void Configure(EntityTypeBuilder<AnnouncementRecipient> builder)
    {
        builder.ToTable("announcement_recipients", "communications");

        // Un destinatario por anuncio; el índice también sirve para "¿este miembro ve este anuncio?".
        builder.HasIndex(r => new { r.AnnouncementId, r.MemberId }).IsUnique();
        builder.HasIndex(r => new { r.MemberId, r.AnnouncementId });

        builder.HasOne<Announcement>()
            .WithMany()
            .HasForeignKey(r => r.AnnouncementId)
            .HasConstraintName("fk_announcement_recipients_announcements")
            .OnDelete(DeleteBehavior.Cascade);
    }
}
