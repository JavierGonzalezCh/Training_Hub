namespace GSM.Application.Achievements.Public;

/// <summary>
/// Evaluación de logros. Es una **escritura** que Achievements expone a otros módulos (Attendance
/// la llama tras un check-in). Idempotente: solo desbloquea los que aún no lo estaban.
/// </summary>
public interface IAchievementService
{
    Task<IReadOnlyList<UnlockedAchievement>> EvaluateForMemberAsync(
        Guid memberId, CancellationToken cancellationToken = default);
}

public sealed record UnlockedAchievement(
    Guid AchievementId,
    string Name,
    string? IconUrl,
    DateTimeOffset UnlockedAt);
