using GSM.Domain.Classes;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace GSM.Infrastructure.Classes;

public sealed class ClassSessionConfiguration : IEntityTypeConfiguration<ClassSession>
{
    public void Configure(EntityTypeBuilder<ClassSession> builder)
    {
        builder.ToTable("class_sessions", "classes");

        builder.Property(s => s.SessionDate).HasColumnType("date");
        builder.Property(s => s.StartTime).HasColumnType("time without time zone");
        builder.Property(s => s.InstructorName).HasMaxLength(120);
        builder.Property(s => s.InstructorUserId).HasColumnName("instructor_user_id");
        builder.HasIndex(s => new { s.GymId, s.InstructorUserId, s.SessionDate });
        builder.Property(s => s.CancellationReason).HasMaxLength(200);

        builder.Property(s => s.Status)
            .HasColumnName("status")
            .HasColumnType("classes.class_session_status");

        builder.HasOne<GymClass>()
            .WithMany()
            .HasForeignKey(s => s.ClassId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasIndex(s => new { s.ClassId, s.SessionDate }).IsUnique();
    }
}
