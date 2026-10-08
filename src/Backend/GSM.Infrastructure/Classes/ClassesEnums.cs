using GSM.Infrastructure.Persistence;

namespace GSM.Infrastructure.Classes;

/// <summary>Enums nativos de Postgres del módulo Classes.</summary>
public sealed class ClassesEnums : IModuleEnums
{
    public IEnumerable<PgEnumMapping> Enums { get; } =
    [
        PgEnumMapping.Of<GSM.Domain.Classes.ClassDayOfWeek>("day_of_week", "classes", LowerCaseNameTranslator.Instance),
        PgEnumMapping.Of<GSM.Domain.Classes.ClassSessionStatus>("class_session_status", "classes", LowerCaseNameTranslator.Instance),
        PgEnumMapping.Of<GSM.Domain.Classes.ClassBookingStatus>("class_booking_status", "classes", SnakeCaseNameTranslator.Instance),
    ];
}
