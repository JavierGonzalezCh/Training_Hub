using GSM.Domain.Common;
using GSM.Domain.Common.Exceptions;

namespace GSM.Domain.Achievements;

public sealed class Achievement : BaseEntity, ITenantEntity
{
    private Achievement()
    {
    }

    public Guid GymId { get; private set; }

    public string Name { get; private set; } = string.Empty;

    public string? Description { get; private set; }

    public string? IconUrl { get; private set; }

    public AchievementConditionType ConditionType { get; private set; }

    public int ConditionValue { get; private set; }

    public bool IsActive { get; private set; } = true;

    public static Achievement Create(
        Guid gymId,
        string name,
        string? description,
        string? iconUrl,
        AchievementConditionType conditionType,
        int conditionValue)
    {
        if (gymId == Guid.Empty)
        {
            throw new ValidationDomainException("El logro necesita un gym.");
        }

        return new Achievement
        {
            GymId = gymId,
            Name = NormalizeName(name),
            Description = Clean(description),
            IconUrl = Clean(iconUrl),
            ConditionType = conditionType,
            ConditionValue = NormalizeValue(conditionType, conditionValue),
        };
    }

    public void Update(string name, string? description, string? iconUrl, AchievementConditionType conditionType, int conditionValue)
    {
        Name = NormalizeName(name);
        Description = Clean(description);
        IconUrl = Clean(iconUrl);
        ConditionType = conditionType;
        ConditionValue = NormalizeValue(conditionType, conditionValue);
    }

    public bool IsManual => ConditionType == AchievementConditionType.Manual;

    public void Deactivate() => IsActive = false;

    public void Activate() => IsActive = true;

    /// <summary>Fija solo el ícono (subida de archivo).</summary>
    public void SetIcon(string? iconUrl) => IconUrl = Clean(iconUrl);

    /// <summary>¿El progreso del miembro alcanza la condición?</summary>
    public bool IsMetBy(
        int visitCount, int currentStreak, int routineSessions, int classAttendance,
        int tenureMonths = 0, int referrals = 0) => ConditionType switch
    {
        AchievementConditionType.Visits => visitCount >= ConditionValue,
        AchievementConditionType.Streak => currentStreak >= ConditionValue,
        AchievementConditionType.RoutineSessions => routineSessions >= ConditionValue,
        AchievementConditionType.Classes => classAttendance >= ConditionValue,
        AchievementConditionType.TenureMonths => tenureMonths >= ConditionValue,
        AchievementConditionType.Referrals => referrals >= ConditionValue,
        _ => false,
    };

    public int ProgressOf(
        int visitCount, int currentStreak, int routineSessions, int classAttendance,
        int tenureMonths = 0, int referrals = 0) => ConditionType switch
    {
        AchievementConditionType.Visits => Math.Min(visitCount, ConditionValue),
        AchievementConditionType.Streak => Math.Min(currentStreak, ConditionValue),
        AchievementConditionType.RoutineSessions => Math.Min(routineSessions, ConditionValue),
        AchievementConditionType.Classes => Math.Min(classAttendance, ConditionValue),
        AchievementConditionType.TenureMonths => Math.Min(tenureMonths, ConditionValue),
        AchievementConditionType.Referrals => Math.Min(referrals, ConditionValue),
        _ => 0,
    };

    private static string NormalizeName(string name)
    {
        var value = (name ?? string.Empty).Trim();
        if (value.Length is < 2 or > 100)
        {
            throw new ValidationDomainException("El nombre del logro debe tener entre 2 y 100 caracteres.");
        }

        return value;
    }

    /// <summary>Un logro manual no tiene condición: el valor se fija en 1 (la columna es obligatoria).</summary>
    private static int NormalizeValue(AchievementConditionType type, int value)
        => type == AchievementConditionType.Manual ? 1 : RequirePositive(value);

    private static int RequirePositive(int value)
        => value > 0 ? value : throw new ValidationDomainException("La condición debe ser mayor que cero.");

    private static string? Clean(string? value)
        => string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}
