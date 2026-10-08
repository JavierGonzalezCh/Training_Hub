using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using AttendanceEntity = GSM.Domain.Attendance.Attendance;

namespace GSM.Infrastructure.Attendance;

public sealed class AttendanceConfiguration : IEntityTypeConfiguration<AttendanceEntity>
{
    public void Configure(EntityTypeBuilder<AttendanceEntity> builder)
    {
        builder.ToTable("attendances", "attendance");

        builder.Property(a => a.Method).HasColumnName("method");
        builder.Property(a => a.AuthorizationReason).HasMaxLength(500);

        // Una asistencia por miembro por día de negocio.
        builder.HasIndex(a => new { a.MemberId, a.AttendanceDate }).IsUnique();
        // Requerido por el plan.
        builder.HasIndex(a => new { a.GymId, a.CheckedInAt });
    }
}
