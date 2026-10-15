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
