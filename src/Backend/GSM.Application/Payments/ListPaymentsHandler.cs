using GSM.Application.Common;
using GSM.Application.Common.Abstractions;
using GSM.Application.Members.Public;
using GSM.Application.Payments.Abstractions;
using GSM.Application.Payments.Contracts;
using GSM.Domain.Common.Exceptions;

namespace GSM.Application.Payments;

public sealed class ListPaymentsHandler(
    ICurrentUserService currentUser,
    IClock clock,
    IMemberReader members,
    IPaymentRepository payments)
{
    public async Task<PagedResult<PaymentResponse>> HandleAsync(
        ListPaymentsQuery query, CancellationToken cancellationToken)
    {
        _ = currentUser.RequireGymId();

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

        return new PagedResult<PaymentResponse>(
            items.Select(p => PaymentMapper.ToResponse(p, reversed)).ToList(), page, pageSize, total);
    }
}
