using GSM.Application.Common.Events;
using GSM.Application.Members.Events;
using GSM.Application.Routines.Abstractions;
using Microsoft.Extensions.Logging;

namespace GSM.Application.Routines;

/// <summary>
/// R10: cuando una ficha se anonimiza, se vacían las notas de sus rutinas, líneas y registros de entrenamiento (texto
/// libre que puede traer datos de salud). Las rutinas, series, pesos y fechas se conservan: quedan ligados a un
/// <c>member_id</c> que ya no identifica a nadie. Idempotente.
/// </summary>
public sealed class ClearRoutineNotesOnMemberAnonymizedHandler(
    IRoutineRepository routines,
    ILogger<ClearRoutineNotesOnMemberAnonymizedHandler> logger) : IDomainEventHandler<MemberAnonymized>
{
    public async Task HandleAsync(MemberAnonymized domainEvent, CancellationToken cancellationToken)
    {
        await routines.ClearNotesForMemberAsync(domainEvent.GymId, domainEvent.MemberId, cancellationToken);
        logger.LogInformation("Notas de rutina del miembro anonimizado {MemberId} vaciadas", domainEvent.MemberId);
    }
}
