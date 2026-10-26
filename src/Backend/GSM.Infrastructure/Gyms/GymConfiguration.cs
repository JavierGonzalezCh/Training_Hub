using GSM.Domain.Gyms;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace GSM.Infrastructure.Gyms;

public sealed class GymConfiguration : IEntityTypeConfiguration<Gym>
{
    public void Configure(EntityTypeBuilder<Gym> builder)
    {
        builder.ToTable("gyms", "gyms");

        builder.Property(g => g.Name).HasMaxLength(100).IsRequired();
        builder.Property(g => g.Slug).HasMaxLength(60).IsRequired();
        builder.HasIndex(g => g.Slug).IsUnique();

        builder.Property(g => g.Nit).HasMaxLength(20);
        builder.Property(g => g.Phone).HasMaxLength(20);
        builder.Property(g => g.Email).HasMaxLength(256);
        builder.Property(g => g.LogoUrl).HasMaxLength(2048);
        builder.Property(g => g.PrimaryColor).HasMaxLength(7).IsRequired();
        builder.Property(g => g.SecondaryColor).HasMaxLength(7).IsRequired();
        builder.Property(g => g.OwnerId).IsRequired();
        builder.Property(g => g.BillingContactUserId);
        builder.Property(g => g.Capacity);

        builder.Property(g => g.LegalName).HasMaxLength(200);
        builder.Property(g => g.Address).HasMaxLength(300);
        builder.Property(g => g.City).HasMaxLength(100);
        builder.Property(g => g.Latitude);
        builder.Property(g => g.Longitude);
        builder.Property(g => g.GeofenceRadiusMeters).HasDefaultValue(Gym.DefaultGeofenceRadiusMeters);
        builder.Property(g => g.InstagramUrl).HasMaxLength(2048);
        builder.Property(g => g.FacebookUrl).HasMaxLength(2048);
        builder.Property(g => g.TikTokUrl).HasMaxLength(2048);
        builder.Property(g => g.WebsiteUrl).HasMaxLength(2048);
        builder.Property(g => g.WhatsAppPhone).HasMaxLength(20);

        builder.Property(g => g.Status).HasColumnName("status");

        builder.Property(g => g.Type).HasColumnName("type").HasDefaultValue(GymType.Gym);

        builder.HasIndex(g => g.OwnerId);
    }
}
