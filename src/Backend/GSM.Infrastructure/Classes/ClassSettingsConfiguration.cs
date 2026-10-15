using GSM.Domain.Classes;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace GSM.Infrastructure.Classes;

public sealed class ClassSettingsConfiguration : IEntityTypeConfiguration<ClassSettings>
{
    public void Configure(EntityTypeBuilder<ClassSettings> builder)
    {
        builder.ToTable("class_settings", "classes");

        builder.HasIndex(s => s.GymId).IsUnique();

        // C4: las filas que ya existían reciben el valor por defecto en vez de 0.
        builder.Property(s => s.WaitlistOfferMinutes).HasDefaultValue(ClassSettings.DefaultWaitlistOfferMinutes);
    }
}

public sealed class BookingBlockConfiguration : IEntityTypeConfiguration<BookingBlock>
{
    public void Configure(EntityTypeBuilder<BookingBlock> builder)
    {
        builder.ToTable("booking_blocks", "classes");

        builder.Property(b => b.LiftReason).HasMaxLength(500);

        builder.HasIndex(b => new { b.MemberId, b.AppliedAt });
    }
}
