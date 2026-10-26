using GSM.Infrastructure.Persistence;

namespace GSM.Infrastructure.Gyms;

/// <summary>Enums nativos de Postgres del módulo Gyms.</summary>
public sealed class GymsEnums : IModuleEnums
{
    public IEnumerable<PgEnumMapping> Enums { get; } =
    [
        PgEnumMapping.Of<GSM.Domain.Gyms.GymStatus>("gym_status", "gyms", LowerCaseNameTranslator.Instance),
        PgEnumMapping.Of<GSM.Domain.Gyms.GymType>("gym_type", "gyms", SnakeCaseNameTranslator.Instance),
    ];
}
