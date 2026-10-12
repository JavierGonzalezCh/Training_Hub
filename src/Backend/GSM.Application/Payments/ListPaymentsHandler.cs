using GSM.Application.Common;
using GSM.Application.Common.Abstractions;
using GSM.Application.Members.Public;
using GSM.Application.Payments.Abstractions;
using GSM.Application.Payments.Contracts;
using GSM.Domain.Common.Exceptions;
using GSM.Domain.Identity;

namespace GSM.Application.Payments;

public sealed class ListPaymentsHandler(
    ICurrentUserService currentUser,
    IClock clock,
    IMemberReader members,
    IPaymentRepository payments,
    IVoidRequestRepository voidRequests)
{
    public async Task<PagedResult<PaymentResponse>> HandleAsync(
        ListPaymentsQuery query, CancellationToken cancellationToken)
    {
        _ = currentUser.RequireGymId();

        // La lista completa del gym es financiera (Payments v2 §7); los pagos de un miembro los ve todo el staff.
        if (query.MemberId is null && !currentUser.HasPermission(StaffPermission.ViewFinances))
        {
            throw new ForbiddenDomainException("No tienes permiso para ver la lista de pagos del gym.");
        }

        if (query.MemberId is { } memberId && !await members.ExistsAsync(memberId, cancellationToken))
        {
            throw new NotFoundException("Miembro no encontrado.");
        }

        var page = query.Page < 1 ? 1 : query.Page;
        var pageSize = Math.Clamp(query.PageSize, 1, 100);

        DateTimeOffset? fromUtc = query.From is { } from ? clock.BusinessDayRange(from).Start : null;
        DateTimeOffset? toUtc = query.To is { } to ? clock.BusinessDayRange(to).End : null;

        var (items, total) = await payments.GetPageAsync(
            new PaymentPageQuery(fromUtc, toUtc, query.Method, query.MemberId, page, pageSize),
            cancellationToken);

        var reversed = await payments.GetReversedIdsAsync(items.Select(p => p.Id).ToList(), cancellationToken);

        var ids = items.Select(p => p.Id).ToList();
        var pending = await voidRequests.GetPendingPaymentIdsAsync(ids, cancellationToken);

        return new PagedResult<PaymentResponse>(
            items.Select(p => PaymentMapper.ToResponse(p, reversed, pending)).ToList(), page, pageSize, total);
    }
}
