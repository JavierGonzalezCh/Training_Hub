using GSM.Application.Attendance.Abstractions;
using GSM.Application.Common.Events;
using GSM.Application.Members.Events;
using Microsoft.Extensions.Logging;

namespace GSM.Application.Attendance;

/// <summary>
/// M6-2: cuando una ficha se anonimiza, el motivo escrito al "Dejar pasar" a un miembro (texto libre) se reemplaza por
/// "[anonimizado]". Quién autorizó y cuándo se conserva. Idempotente.
/// </summary>
public sealed class ClearAuthorizationReasonOnMemberAnonymizedHandler(
    IAttendanceRepository attendances,
    ILogger<ClearAuthorizationReasonOnMemberAnonymizedHandler> logger) : IDomainEventHandler<MemberAnonymized>
{
    public const string Replacement = "[anonimizado]";

    public async Task HandleAsync(MemberAnonymized domainEvent, CancellationToken cancellationToken)
    {
        await attendances.ReplaceAuthorizationReasonsForMemberAsync(
            domainEvent.GymId, domainEvent.MemberId, Replacement, cancellationToken);
        logger.LogInformation("Motivos de entrada del miembro anonimizado {MemberId} reemplazados", domainEvent.MemberId);
    }
}
