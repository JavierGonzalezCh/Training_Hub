namespace GSM.Application.Achievements.Public;

/// <summary>
/// Evaluación de logros. Es una **escritura** que Achievements expone a otros módulos (Attendance
/// la llama tras un check-in). Idempotente: solo desbloquea los que aún no lo estaban.
/// </summary>
public interface IAchievementService
{
    Task<IReadOnlyList<UnlockedAchievement>> EvaluateForMemberAsync(
        Guid memberId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Estado de congeladores del miembro con **concesión perezosa**: si han pasado ≥ 15 días desde
    /// el último <c>granted_on</c> y hay menos de 2 disponibles, otorga uno y lo persiste.
    /// Consultar la racha puede escribir: es deliberado, no hay proceso diario.
    /// </summary>
    Task<StreakFreezeState> GetOrGrantStreakFreezesAsync(
        Guid memberId, DateOnly today, CancellationToken cancellationToken = default);

    /// <summary>
    /// Consume un congelador por cada fecha fallada que aún no tenga uno. Idempotente
    /// (índice único parcial). Devuelve el saldo disponible resultante.
    /// </summary>
    Task<int> ConsumeStreakFreezesAsync(
        Guid memberId, IReadOnlyCollection<DateOnly> failedDates, CancellationToken cancellationToken = default);
}

/// <summary>Saldo de congeladores y las fechas ya cubiertas por uno.</summary>
public sealed record StreakFreezeState(int Available, IReadOnlySet<DateOnly> SpentOn);

public sealed record UnlockedAchievement(
    Guid AchievementId,
    string Name,
    string? IconUrl,
    DateTimeOffset UnlockedAt);
