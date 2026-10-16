using GSM.Application.Achievements.Public;
using GSM.Application.Common.Events;
using GSM.Application.Routines.Events;
using Microsoft.Extensions.Logging;

namespace GSM.Application.Achievements.Events;

/// <summary>
/// Un récord personal nuevo (R7) reevalúa los logros de récords y de volumen (L3). Ignora los de rutinas creadas por el
/// propio miembro (<c>SelfAssigned</c>). Idempotente por construcción: las métricas se recalculan desde los registros
/// y <c>EvaluateForMemberAsync</c> solo desbloquea lo que aún no estaba desbloqueado, así que un evento repetido por
/// <c>(MemberId, ExerciseId, PerformedOn, Kind)</c> no duplica nada.
/// </summary>
public sealed class PersonalRecordSetHandler(
    IAchievementService achievements,
    ILogger<PersonalRecordSetHandler> logger) : IDomainEventHandler<PersonalRecordSet>
{
    public async Task HandleAsync(PersonalRecordSet domainEvent, CancellationToken cancellationToken = default)
    {
        if (domainEvent.SelfAssigned)
        {
            return;
        }

        var unlocked = await achievements.EvaluateForMemberAsync(domainEvent.MemberId, cancellationToken);
        if (unlocked.Count > 0)
        {
            logger.LogInformation(
                "Un récord desbloqueó {Count} logro(s) para el miembro {MemberId}", unlocked.Count, domainEvent.MemberId);
        }
    }
}
