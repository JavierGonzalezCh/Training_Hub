using GSM.Infrastructure.Persistence;

namespace GSM.Infrastructure.Attendance;

/// <summary>Enums nativos de Postgres del módulo Attendance.</summary>
public sealed class AttendanceEnums : IModuleEnums
{
    public IEnumerable<PgEnumMapping> Enums { get; } =
    [
        PgEnumMapping.Of<GSM.Domain.Attendance.AttendanceMethod>("attendance_method", "attendance", LowerCaseNameTranslator.Instance),
        PgEnumMapping.Of<GSM.Domain.Attendance.LocationCheck>("location_check", "attendance", LowerCaseNameTranslator.Instance),
        PgEnumMapping.Of<GSM.Domain.Attendance.CheckinMode>("checkin_mode", "attendance", LowerCaseNameTranslator.Instance),
        PgEnumMapping.Of<GSM.Domain.Attendance.CheckinRejectionReason>("checkin_rejection_reason", "attendance", SnakeCaseNameTranslator.Instance),
    ];
}
