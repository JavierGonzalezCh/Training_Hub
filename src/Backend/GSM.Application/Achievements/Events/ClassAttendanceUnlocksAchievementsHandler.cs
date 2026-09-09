using GSM.Application.Achievements.Public;
using GSM.Application.Classes.Events;
using GSM.Application.Common.Events;
using Microsoft.Extensions.Logging;

namespace GSM.Application.Achievements.Events;

/// <summary>
/// Asistir a una clase dispara la evaluación de logros (condición <c>classes</c> y la racha, ya
/// que la clase también cuenta como check-in del día). Idempotente: <c>AchievementService</c> solo
/// mira los logros que el socio aún no tiene.
/// </summary>
public sealed class ClassAttendanceUnlocksAchievementsHandler(
    IAchievementService achievements,
    ILogger<ClassAttendanceUnlocksAchievementsHandler> logger)
    : IDomainEventHandler<ClassAttendanceRegistered>
{
    public async Task HandleAsync(
        ClassAttendanceRegistered domainEvent, CancellationToken cancellationToken = default)
    {
        var unlocked = await achievements.EvaluateForMemberAsync(domainEvent.MemberId, cancellationToken);
        if (unlocked.Count > 0)
        {
            logger.LogInformation(
                "Asistencia a clase desbloqueó {Count} logro(s) para el socio {MemberId}",
                unlocked.Count, domainEvent.MemberId);
        }
    }
}
