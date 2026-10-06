using GSM.Domain.Attendance;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace GSM.Infrastructure.Attendance;

public sealed class CheckinSettingsConfiguration : IEntityTypeConfiguration<CheckinSettings>
{
    public void Configure(EntityTypeBuilder<CheckinSettings> builder)
    {
        builder.ToTable("checkin_settings", "attendance");

        builder.Property(s => s.Mode).HasColumnName("mode");
        builder.HasIndex(s => s.GymId).IsUnique();
    }
}
