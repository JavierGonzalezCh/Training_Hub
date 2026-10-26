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
        builder.Property(s => s.ByDocument).HasColumnName("by_document").HasDefaultValue(false);
        builder.Property(s => s.AssumedStayMinutes).HasColumnName("assumed_stay_minutes")
            .HasDefaultValue(CheckinSettings.DefaultAssumedStayMinutes);
        builder.Property(s => s.RemoteTraining).HasColumnName("remote_training").HasDefaultValue(false);
        builder.Property(s => s.TrainingEntryTrigger).HasColumnName("training_entry_trigger")
            .HasDefaultValue(TrainingEntryTrigger.Start);
        builder.HasIndex(s => s.GymId).IsUnique();
    }
}
