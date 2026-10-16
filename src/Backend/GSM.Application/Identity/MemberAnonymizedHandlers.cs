using GSM.Application.Common.Events;
using GSM.Application.Identity.Abstractions;
using GSM.Application.Identity.Events;
using GSM.Application.Members.Events;
using Microsoft.Extensions.Logging;

namespace GSM.Application.Identity;

/// <summary>
/// Cuando una ficha se anonimiza, su cuenta de la app pierde el acceso de miembro a ese gym y las sesiones de ese
/// acceso se revocan (Members §11). Si era su único acceso, la cuenta se elimina y se publica
/// <see cref="UserAccountDeleted"/>. Idempotente: sin cuenta o sin acceso no hace nada.
/// </summary>
public sealed class RemoveMemberAccessOnMemberAnonymizedHandler(
    IMemberAccessRemoval removal,
    IDomainEventPublisher events,
    ILogger<RemoveMemberAccessOnMemberAnonymizedHandler> logger) : IDomainEventHandler<MemberAnonymized>
{
    public async Task HandleAsync(MemberAnonymized domainEvent, CancellationToken cancellationToken)
    {
        if (domainEvent.UserId is not { } userId)
        {
            return;
        }

        var result = await removal.RemoveAsync(userId, domainEvent.GymId, cancellationToken);
        if (result.AccountDeleted)
        {
            await events.PublishAsync(new UserAccountDeleted(userId), cancellationToken);
        }

        logger.LogInformation(
            "Acceso de miembro de la cuenta {UserId} al gym {GymId} retirado por anonimización (cuenta eliminada: {Deleted})",
            userId, domainEvent.GymId, result.AccountDeleted);
    }
}
