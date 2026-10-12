using GSM.Application.Attendance.Abstractions;
using GSM.Application.Attendance.Contracts;
using GSM.Application.Common;
using GSM.Application.Common.Abstractions;
using GSM.Application.Common.Events;
using GSM.Application.Members.Public;
using GSM.Domain.Attendance;
using GSM.Domain.Common.Exceptions;
using GSM.Domain.Members;

namespace GSM.Application.Attendance;

public sealed class ListCheckinAttemptsHandler(
    ICurrentUserService currentUser, IMemberReader members, ICheckinAttemptRepository attempts)
{
    public async Task<PagedResult<CheckinAttemptItem>> HandleAsync(
        ListCheckinAttemptsQuery query, CancellationToken cancellationToken)
    {
        _ = currentUser.RequireGymId();

        var page = query.Page < 1 ? 1 : query.Page;
        var pageSize = Math.Clamp(query.PageSize, 1, 100);

        CheckinRejectionReason? reason = null;
        if (!string.IsNullOrWhiteSpace(query.Reason))
        {
            reason = Enum.GetValues<CheckinRejectionReason>()
                .Cast<CheckinRejectionReason?>()
                .FirstOrDefault(r => EnumText.Snake(r) == query.Reason.Trim().ToLowerInvariant())
                ?? throw new ValidationDomainException("El motivo no existe.");
        }

        var (items, total) = await attempts.GetPageAsync(
            new AttemptPageQuery(query.From, query.To, query.PendingOnly, page, pageSize, reason), cancellationToken);

        var names = await ListAttendanceHandler.ResolveNamesAsync(
            members, items.Select(a => a.MemberId), cancellationToken);

        return new PagedResult<CheckinAttemptItem>(
            items.Select(a => new CheckinAttemptItem(
                a.Id, a.MemberId, names.GetValueOrDefault(a.MemberId, "?"), a.AttemptedAt,
                a.AttemptCount, EnumText.Snake(a.Reason)!, a.MembershipEndedOn, a.AcknowledgedAt)).ToList(),
            page, pageSize, total);
    }
}

/// <summary>
/// Cuando el staff atiende el aviso de un intento rechazado (Notifications), el intento queda visto:
/// así un nuevo intento del miembro, ya atendido, crea fila nueva en vez de sumarse a la anterior.
/// </summary>
public sealed class CheckinAttemptResolvedHandler(
    ICheckinAttemptRepository attempts, IUnitOfWork unitOfWork) : IDomainEventHandler<ActionNoticeResolved>
{
    public async Task HandleAsync(ActionNoticeResolved domainEvent, CancellationToken cancellationToken = default)
    {
        if (domainEvent.Kind != ActionNoticeKinds.CheckinRejected)
        {
            return;
        }

        var attempt = await attempts.GetByIdAsync(domainEvent.ReferenceId, cancellationToken);
        if (attempt is null)
        {
            return;
        }

        attempt.Acknowledge(domainEvent.ResolvedBy, domainEvent.ResolvedAt);
        await unitOfWork.SaveChangesAsync(cancellationToken);
    }
}
