using GSM.Application.Common.Abstractions;
using GSM.Application.Common.Events;
using GSM.Application.Memberships.Events;
using GSM.Domain.Notifications;

namespace GSM.Application.Notifications;

/// <summary>
/// Referencias de los recordatorios de vencimiento de una membresía. El de "N días antes" usa el id de la
/// membresía como referencia (como siempre); los otros dos, un id estable derivado de la membresía y el paso,
/// así cada uno es único por <c>(tipo, miembro, referencia)</c> sin tocar el enum de tipos.
/// </summary>
internal static class MembershipReminderReferences
{
    public const string ExpiresToday = "expires_today";

    public const string LastGraceDay = "last_grace_day";

    public static Guid For(Guid membershipId, string step)
        => new(System.Security.Cryptography.MD5.HashData(
            System.Text.Encoding.UTF8.GetBytes($"{membershipId:N}:{step}")));

    /// <summary>Las tres referencias que puede tener una membresía: N días antes, vence hoy y último día de gracia.</summary>
    public static Guid[] All(Guid membershipId)
        => [membershipId, For(membershipId, ExpiresToday), For(membershipId, LastGraceDay)];
}

/// <summary>
/// N5-2: un recordatorio de vencimiento ya programado no debe salir si el miembro ya renovó. Al renovar (la
/// membresía nueva se encadena tras la anterior) se cancelan los recordatorios que la anterior aún tenga sin
/// enviar. Idempotente: lo ya enviado o ya cancelado no se toca.
/// </summary>
public sealed class MembershipRenewedCancelsReminders(
    ScheduledNoticeService scheduler,
    IUnitOfWork unitOfWork) : IDomainEventHandler<MembershipRenewed>
{
    public async Task HandleAsync(MembershipRenewed domainEvent, CancellationToken cancellationToken = default)
        => await MembershipReminderCancellation.CancelAsync(
            scheduler, unitOfWork, domainEvent.MemberId, domainEvent.PreviousMembershipId, cancellationToken);
}

/// <summary>N5-2: si la membresía se cancela (o se cambia de plan), sus recordatorios de vencimiento ya no aplican.</summary>
public sealed class MembershipCancelledCancelsReminders(
    ScheduledNoticeService scheduler,
    IUnitOfWork unitOfWork) : IDomainEventHandler<MembershipCancelled>
{
    public async Task HandleAsync(MembershipCancelled domainEvent, CancellationToken cancellationToken = default)
        => await MembershipReminderCancellation.CancelAsync(
            scheduler, unitOfWork, domainEvent.MemberId, domainEvent.MembershipId, cancellationToken);
}

internal static class MembershipReminderCancellation
{
    public static async Task CancelAsync(
        ScheduledNoticeService scheduler, IUnitOfWork unitOfWork,
        Guid memberId, Guid membershipId, CancellationToken cancellationToken)
    {
        var any = false;
        foreach (var reference in MembershipReminderReferences.All(membershipId))
        {
            any |= await scheduler.CancelForMemberAsync(
                NotificationType.MembershipExpiring, memberId, reference, cancellationToken);
        }

        if (any)
        {
            await unitOfWork.SaveChangesAsync(cancellationToken);
        }
    }
}
