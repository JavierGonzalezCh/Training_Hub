using GSM.Infrastructure.Persistence;

namespace GSM.Infrastructure.Achievements;

/// <summary>Enums nativos de Postgres del módulo Achievements.</summary>
public sealed class AchievementsEnums : IModuleEnums
{
    public IEnumerable<PgEnumMapping> Enums { get; } =
    [
        PgEnumMapping.Of<GSM.Domain.Achievements.AchievementConditionType>("condition_type", "achievements", SnakeCaseNameTranslator.Instance),
    ];
}
