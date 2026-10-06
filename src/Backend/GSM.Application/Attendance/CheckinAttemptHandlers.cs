using GSM.Application.Attendance.Abstractions;
using GSM.Application.Attendance.Contracts;
using GSM.Application.Common;
using GSM.Application.Common.Abstractions;
using GSM.Application.Members.Public;
using GSM.Domain.Common.Exceptions;

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

        var (items, total) = await attempts.GetPageAsync(
            new AttemptPageQuery(query.From, query.To, query.PendingOnly, page, pageSize), cancellationToken);

        var names = await ListAttendanceHandler.ResolveNamesAsync(
            members, items.Select(a => a.MemberId), cancellationToken);

        return new PagedResult<CheckinAttemptItem>(
            items.Select(a => new CheckinAttemptItem(
                a.Id, a.MemberId, names.GetValueOrDefault(a.MemberId, "?"), a.AttemptedAt,
                a.AttemptCount, a.Reason.ToString(), a.MembershipEndedOn, a.AcknowledgedAt)).ToList(),
            page, pageSize, total);
    }
}

public sealed class GetPendingAttemptCountHandler(
    ICurrentUserService currentUser, ICheckinAttemptRepository attempts)
{
    public async Task<PendingAttemptCountResponse> HandleAsync(CancellationToken cancellationToken)
    {
        _ = currentUser.RequireGymId();
        return new PendingAttemptCountResponse(await attempts.GetPendingCountAsync(cancellationToken));
    }
}

public sealed class AcknowledgeCheckinAttemptHandler(
    ICurrentUserService currentUser, IClock clock, ICheckinAttemptRepository attempts, IUnitOfWork unitOfWork)
{
    public async Task HandleAsync(Guid attemptId, CancellationToken cancellationToken)
    {
        _ = currentUser.RequireGymId();
        var userId = currentUser.UserId ?? throw new UnauthorizedDomainException("Sin usuario en el contexto.");

        var attempt = await attempts.GetByIdAsync(attemptId, cancellationToken)
            ?? throw new NotFoundException("Intento no encontrado.");
        attempt.Acknowledge(userId, clock.UtcNow);
        await unitOfWork.SaveChangesAsync(cancellationToken);
    }
}

public sealed class AcknowledgeAllCheckinAttemptsHandler(
    ICurrentUserService currentUser, IClock clock, ICheckinAttemptRepository attempts, IUnitOfWork unitOfWork)
{
    public async Task HandleAsync(CancellationToken cancellationToken)
    {
        _ = currentUser.RequireGymId();
        var userId = currentUser.UserId ?? throw new UnauthorizedDomainException("Sin usuario en el contexto.");

        var now = clock.UtcNow;
        foreach (var attempt in await attempts.GetPendingAsync(cancellationToken))
        {
            attempt.Acknowledge(userId, now);
        }

        await unitOfWork.SaveChangesAsync(cancellationToken);
    }
}
