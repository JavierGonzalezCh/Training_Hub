using GSM.Application.Common.Events;
using GSM.Application.Equipment.Abstractions;
using GSM.Application.Members.Events;
using Microsoft.Extensions.Logging;

namespace GSM.Application.Equipment;

/// <summary>
/// Equipment §9: los reportes de un miembro anonimizado se conservan (son el historial de la máquina); solo se vacía
/// quién los hizo. La descripción se conserva: habla de la máquina, no de la persona. Idempotente.
/// </summary>
public sealed class ClearIssueReporterOnMemberAnonymizedHandler(
    IEquipmentIssueRepository issues,
    ILogger<ClearIssueReporterOnMemberAnonymizedHandler> logger) : IDomainEventHandler<MemberAnonymized>
{
    public async Task HandleAsync(MemberAnonymized domainEvent, CancellationToken cancellationToken)
    {
        if (domainEvent.UserId is not { } userId)
        {
            return;
        }

        await issues.ClearReporterAsync(domainEvent.GymId, userId, cancellationToken);
        logger.LogInformation("Reportes de equipos del miembro anonimizado {MemberId} sin autor", domainEvent.MemberId);
    }
}
