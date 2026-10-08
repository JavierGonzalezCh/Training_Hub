using GSM.Infrastructure.Persistence;

namespace GSM.Infrastructure.Routines;

/// <summary>Enums nativos de Postgres del módulo Routines.</summary>
public sealed class RoutinesEnums : IModuleEnums
{
    public IEnumerable<PgEnumMapping> Enums { get; } =
    [
        PgEnumMapping.Of<GSM.Domain.Routines.Muscle>("muscle", "routines", LowerCaseNameTranslator.Instance),
        PgEnumMapping.Of<GSM.Domain.Routines.Equipment>("equipment", "routines", LowerCaseNameTranslator.Instance),
        PgEnumMapping.Of<GSM.Domain.Routines.ExerciseType>("exercise_type", "routines", LowerCaseNameTranslator.Instance),
        PgEnumMapping.Of<GSM.Domain.Routines.TrackingType>("tracking_type", "routines", SnakeCaseNameTranslator.Instance),
        PgEnumMapping.Of<GSM.Domain.Routines.Difficulty>("difficulty", "routines", LowerCaseNameTranslator.Instance),
        PgEnumMapping.Of<GSM.Domain.Routines.ScheduleMode>("schedule_mode", "routines", LowerCaseNameTranslator.Instance),
    ];
}
