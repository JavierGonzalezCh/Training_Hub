using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace GSM.Infrastructure.Identity;

public sealed class KioskDeviceConfiguration : IEntityTypeConfiguration<KioskDevice>
{
    public void Configure(EntityTypeBuilder<KioskDevice> builder)
    {
        builder.ToTable("kiosk_devices", "identity");

        builder.Property(k => k.Name).HasMaxLength(100).IsRequired();
        builder.Property(k => k.PairingCodeHash).HasMaxLength(64);
        builder.Property(k => k.RefreshTokenHash).HasMaxLength(64);

        builder.HasIndex(k => k.GymId);
        builder.HasIndex(k => k.PairingCodeHash).IsUnique().HasFilter("pairing_code_hash IS NOT NULL");
        builder.HasIndex(k => k.RefreshTokenHash).IsUnique().HasFilter("refresh_token_hash IS NOT NULL");
    }
}
