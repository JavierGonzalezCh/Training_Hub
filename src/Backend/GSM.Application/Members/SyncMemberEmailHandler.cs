using GSM.Application.Common.Abstractions;
using GSM.Application.Common.Events;
using GSM.Application.Identity.Events;
using GSM.Application.Members.Abstractions;
using Microsoft.Extensions.Logging;

namespace GSM.Application.Members;

/// <summary>
/// Cuando la persona confirma un email nuevo en su cuenta, todas las fichas vinculadas a ella (en todos sus
/// gyms) toman ese email: "un solo email, manda Members" sin que dos gyms tengan emails distintos para la misma
/// persona (Identity §3.4). Idempotente: una ficha que ya tiene el email no cambia ni deja fila de historial.
/// </summary>
public sealed class SyncMemberEmailOnUserEmailChangedHandler(
    IMemberRepository members,
    MemberChangeRecorder changes,
    IUnitOfWork unitOfWork,
    ILogger<SyncMemberEmailOnUserEmailChangedHandler> logger)
    : IDomainEventHandler<UserEmailChanged>
{
    public async Task HandleAsync(UserEmailChanged domainEvent, CancellationToken cancellationToken)
    {
        var linked = await members.GetByUserAcrossGymsAsync(domainEvent.UserId, cancellationToken);

        var updated = 0;
        foreach (var member in linked)
        {
            var change = member.ChangeEmail(domainEvent.NewEmail);
            if (change is null)
            {
                continue;
            }

            changes.Record(member, [change]);
            updated++;
        }

        if (updated == 0)
        {
            return;
        }

        await unitOfWork.SaveChangesAsync(cancellationToken);
        logger.LogInformation(
            "Email de {Count} fichas de miembro actualizado por el cambio de email de la cuenta {UserId}",
            updated, domainEvent.UserId);
    }
}
