using GSM.Application.Common.Events;
using GSM.Application.Members.Events;
using GSM.Application.Memberships.Abstractions;
using Microsoft.Extensions.Logging;

namespace GSM.Application.Memberships;

/// <summary>
/// M6-2: cuando una ficha se anonimiza, los motivos de cancelación y de congelamiento de sus membresías (texto libre)
/// se reemplazan por "[anonimizado]". Fechas, planes y días acreditados se conservan. Idempotente.
/// </summary>
public sealed class ClearMembershipReasonsOnMemberAnonymizedHandler(
    IMembershipRepository memberships,
    ILogger<ClearMembershipReasonsOnMemberAnonymizedHandler> logger) : IDomainEventHandler<MemberAnonymized>
{
    public const string Replacement = "[anonimizado]";

    public async Task HandleAsync(MemberAnonymized domainEvent, CancellationToken cancellationToken)
    {
        await memberships.ReplaceReasonsForMemberAsync(
            domainEvent.GymId, domainEvent.MemberId, Replacement, cancellationToken);
        logger.LogInformation("Motivos de membresías del miembro anonimizado {MemberId} reemplazados", domainEvent.MemberId);
    }
}
