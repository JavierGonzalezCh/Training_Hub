using GSM.Application.Common.Events;
using GSM.Application.Members.Events;
using GSM.Application.Payments.Abstractions;
using Microsoft.Extensions.Logging;

namespace GSM.Application.Payments;

/// <summary>
/// M6-2: cuando una ficha se anonimiza, las notas de sus pagos (texto libre) se reemplazan por "[anonimizado]". Los
/// montos, métodos y fechas se conservan: son contabilidad. Idempotente.
/// </summary>
public sealed class ClearPaymentNotesOnMemberAnonymizedHandler(
    IPaymentRepository payments,
    ILogger<ClearPaymentNotesOnMemberAnonymizedHandler> logger) : IDomainEventHandler<MemberAnonymized>
{
    public const string Replacement = "[anonimizado]";

    public async Task HandleAsync(MemberAnonymized domainEvent, CancellationToken cancellationToken)
    {
        await payments.ReplaceNotesForMemberAsync(
            domainEvent.GymId, domainEvent.MemberId, Replacement, cancellationToken);
        logger.LogInformation("Notas de pagos del miembro anonimizado {MemberId} reemplazadas", domainEvent.MemberId);
    }
}
