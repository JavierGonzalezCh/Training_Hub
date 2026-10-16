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

        // A1b: con entradas múltiples puede haber varias filas el mismo día; "una por día" y el máximo
        // diario se validan en el caso de uso (antes era un índice único).
        builder.Property(a => a.VoidReason).HasMaxLength(500);
        builder.Ignore(a => a.IsVoided);

        // A6: resultado de la geocerca con el QR fijo (nulo = no se pidió). Nunca se guardan coordenadas.
        builder.Property(a => a.LocationCheck).HasColumnName("location_check");
        builder.Ignore(a => a.CountsForProgress);

        builder.HasIndex(a => new { a.MemberId, a.AttendanceDate });
        // Requerido por el plan.
        builder.HasIndex(a => new { a.GymId, a.CheckedInAt });
    }
}
