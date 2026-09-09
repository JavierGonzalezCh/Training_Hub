using GSM.Domain.Classes;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace GSM.Infrastructure.Classes;

public sealed class GymClassConfiguration : IEntityTypeConfiguration<GymClass>
{
    public void Configure(EntityTypeBuilder<GymClass> builder)
    {
        builder.ToTable("gym_classes", "classes");

        builder.Property(c => c.Name).HasMaxLength(120).IsRequired();
        builder.Property(c => c.Description).HasMaxLength(1000);
        builder.Property(c => c.InstructorName).HasMaxLength(120);

        builder.Property(c => c.StartTime).HasColumnType("time without time zone");

        builder.Property(c => c.DaysOfWeek)
            .HasColumnName("days_of_week")
            .HasColumnType("classes.day_of_week[]");

        builder.Property(c => c.IsActive).HasDefaultValue(true);

        builder.HasIndex(c => new { c.GymId, c.IsActive });
        builder.HasIndex(c => new { c.GymId, c.ScheduledDate });
    }
}
