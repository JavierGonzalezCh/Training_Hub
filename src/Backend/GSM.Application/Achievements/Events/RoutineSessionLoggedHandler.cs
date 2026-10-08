using GSM.Application.Achievements.Public;
using GSM.Application.Common.Events;
using GSM.Application.Routines.Events;
using Microsoft.Extensions.Logging;

namespace GSM.Application.Achievements.Events;

/// <summary>
/// Cada sesión de rutina registrada dispara la evaluación de logros (condición
/// <c>routine_sessions</c> y la racha que puntúa). Las sesiones de rutinas auto-asignadas
/// no cuentan: el filtro vive en <c>IRoutineReader</c>, así que basta con reevaluar.
/// </summary>
public sealed class RoutineSessionLoggedHandler(
    IAchievementService achievements,
    ILogger<RoutineSessionLoggedHandler> logger) : IDomainEventHandler<RoutineSessionLogged>
{
    public async Task HandleAsync(RoutineSessionLogged domainEvent, CancellationToken cancellationToken = default)
    {
        var unlocked = await achievements.EvaluateForMemberAsync(domainEvent.MemberId, cancellationToken);
        if (unlocked.Count > 0)
        {
            logger.LogInformation(
                "Sesión de rutina desbloqueó {Count} logro(s) para el miembro {MemberId}",
                unlocked.Count, domainEvent.MemberId);
        }
    }
}
