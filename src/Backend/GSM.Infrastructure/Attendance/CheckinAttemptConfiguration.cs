using GSM.Domain.Attendance;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace GSM.Infrastructure.Attendance;

public sealed class CheckinAttemptConfiguration : IEntityTypeConfiguration<CheckinAttempt>
{
    public void Configure(EntityTypeBuilder<CheckinAttempt> builder)
    {
        builder.ToTable("checkin_attempts", "attendance");

        builder.Property(a => a.Reason).HasColumnName("reason");
        builder.HasIndex(a => new { a.GymId, a.AttemptDate });
        builder.HasIndex(a => new { a.GymId, a.AcknowledgedAt });
    }
}
