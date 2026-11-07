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

    /// <summary>Premio opcional del logro (L5): "Camiseta del gym". Nulo = el logro es solo reconocimiento.</summary>
    public string? RewardDescription { get; private set; }

    /// <summary>Días que tiene el miembro para reclamar el premio desde que gana el logro. Nulo = no vence.</summary>
    public int? RewardValidDays { get; private set; }

    public bool HasReward => RewardDescription is not null;

    public static Achievement Create(
        Guid gymId,
        string name,
        string? description,
        string? iconUrl,
        AchievementConditionType conditionType,
        int conditionValue,
        string? rewardDescription = null,
        int? rewardValidDays = null)
    {
        if (gymId == Guid.Empty)
        {
            throw new ValidationDomainException("El logro necesita un gym.");
        }

        var (reward, validDays) = NormalizeReward(rewardDescription, rewardValidDays);
        return new Achievement
        {
            RewardDescription = reward,
            RewardValidDays = validDays,
            GymId = gymId,
            Name = NormalizeName(name),
            Description = Clean(description),
            IconUrl = Clean(iconUrl),
            ConditionType = conditionType,
            ConditionValue = NormalizeValue(conditionType, conditionValue),
        };
    }

    /// <summary>El premio nuevo solo rige para los logros que se ganen desde ahora: los ya ganados conservan el suyo.</summary>
    public void Update(
        string name, string? description, string? iconUrl, AchievementConditionType conditionType, int conditionValue,
        string? rewardDescription = null, int? rewardValidDays = null)
    {
        (RewardDescription, RewardValidDays) = NormalizeReward(rewardDescription, rewardValidDays);
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
        int tenureMonths = 0, int referrals = 0, int personalRecords = 0, int liftedVolumeKg = 0,
        AchievementExtraMetrics? extra = null) => ConditionType switch
    {
        AchievementConditionType.EarlyVisits => (extra?.EarlyVisits ?? 0) >= ConditionValue,
        AchievementConditionType.LateVisits => (extra?.LateVisits ?? 0) >= ConditionValue,
        AchievementConditionType.Weekends => (extra?.Weekends ?? 0) >= ConditionValue,
        AchievementConditionType.CompleteSessions => (extra?.CompleteSessions ?? 0) >= ConditionValue,
        AchievementConditionType.DistinctClasses => (extra?.DistinctClasses ?? 0) >= ConditionValue,
        AchievementConditionType.PowerLifts => (extra?.PowerLifts ?? 0) >= ConditionValue,
        AchievementConditionType.MonthlyRankingTop => extra?.BestMonthlyPosition is { } best && best <= ConditionValue,
        AchievementConditionType.Visits => visitCount >= ConditionValue,
        AchievementConditionType.Streak => currentStreak >= ConditionValue,
        AchievementConditionType.RoutineSessions => routineSessions >= ConditionValue,
        AchievementConditionType.Classes => classAttendance >= ConditionValue,
        AchievementConditionType.TenureMonths => tenureMonths >= ConditionValue,
        AchievementConditionType.Referrals => referrals >= ConditionValue,
        AchievementConditionType.PersonalRecords => personalRecords >= ConditionValue,
        AchievementConditionType.LiftedVolume => liftedVolumeKg >= ConditionValue,
        _ => false,
    };

    public int ProgressOf(
        int visitCount, int currentStreak, int routineSessions, int classAttendance,
        int tenureMonths = 0, int referrals = 0, int personalRecords = 0, int liftedVolumeKg = 0,
        AchievementExtraMetrics? extra = null) => ConditionType switch
    {
        AchievementConditionType.EarlyVisits => Math.Min(extra?.EarlyVisits ?? 0, ConditionValue),
        AchievementConditionType.LateVisits => Math.Min(extra?.LateVisits ?? 0, ConditionValue),
        AchievementConditionType.Weekends => Math.Min(extra?.Weekends ?? 0, ConditionValue),
        AchievementConditionType.CompleteSessions => Math.Min(extra?.CompleteSessions ?? 0, ConditionValue),
        AchievementConditionType.DistinctClasses => Math.Min(extra?.DistinctClasses ?? 0, ConditionValue),
        AchievementConditionType.PowerLifts => Math.Min(extra?.PowerLifts ?? 0, ConditionValue),
        // Un puesto no avanza de a poco: o ya lo logró (meta completa) o no.
        AchievementConditionType.MonthlyRankingTop => extra?.BestMonthlyPosition is { } best && best <= ConditionValue ? ConditionValue : 0,
        AchievementConditionType.Visits => Math.Min(visitCount, ConditionValue),
        AchievementConditionType.Streak => Math.Min(currentStreak, ConditionValue),
        AchievementConditionType.RoutineSessions => Math.Min(routineSessions, ConditionValue),
        AchievementConditionType.Classes => Math.Min(classAttendance, ConditionValue),
        AchievementConditionType.TenureMonths => Math.Min(tenureMonths, ConditionValue),
        AchievementConditionType.Referrals => Math.Min(referrals, ConditionValue),
        AchievementConditionType.PersonalRecords => Math.Min(personalRecords, ConditionValue),
        AchievementConditionType.LiftedVolume => Math.Min(liftedVolumeKg, ConditionValue),
        _ => 0,
    };

    /// <summary>El texto del premio va de 3 a 200 caracteres; el plazo (1 a 730 días) solo tiene sentido con premio.</summary>
    private static (string? Description, int? ValidDays) NormalizeReward(string? description, int? validDays)
    {
        var text = Clean(description);
        if (text is null)
        {
            return (null, null);
        }

        if (text.Length is < 3 or > 200)
        {
            throw new ValidationDomainException("El premio debe tener entre 3 y 200 caracteres.");
        }

        if (validDays is { } days && (days < 1 || days > 730))
        {
            throw new ValidationDomainException("El plazo del premio debe estar entre 1 y 730 días.");
        }

        return (text, validDays);
    }

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
