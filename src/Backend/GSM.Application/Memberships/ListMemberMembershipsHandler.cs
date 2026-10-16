using GSM.Application.Common.Abstractions;
using GSM.Application.Members.Public;
using GSM.Application.Memberships.Abstractions;
using GSM.Application.Memberships.Contracts;
using GSM.Domain.Common.Exceptions;

namespace GSM.Application.Memberships;

public sealed class ListMemberMembershipsHandler(
    ICurrentUserService currentUser,
    IClock clock,
    IMemberReader members,
    IMembershipRepository memberships,
    IMembershipPaidAmountProvider paidAmounts)
{
    public async Task<IReadOnlyList<MembershipResponse>> HandleAsync(
        Guid memberId, CancellationToken cancellationToken)
    {
        _ = currentUser.RequireGymId();

        if (!await members.ExistsAsync(memberId, cancellationToken))
        {
            throw new NotFoundException("Miembro no encontrado.");
        }

        var today = clock.Today;
        var history = await memberships.GetHistoryAsync(memberId, cancellationToken);
        var planNames = await memberships.GetPlanNamesAsync(
            history.Select(m => m.PlanId).Distinct().ToList(), cancellationToken);

        var freezes = await memberships.GetFreezesAsync(history.Select(m => m.Id).ToList(), cancellationToken);
        var used = await memberships.GetPassUsedCountsAsync(
            history.Where(m => m.IsPass).Select(m => m.Id).ToList(), cancellationToken);

        // MS2-3: el servidor no impide una membresía sin pago (el cobro es una segunda llamada); la ficha muestra lo
        // que falta por cobrar para que no se pierda.
        var chargeable = history
            .Where(m => m.Status != Domain.Memberships.MembershipStatus.Cancelled && m.TotalToCharge > 0)
            .Select(m => m.Id)
            .ToList();
        var paid = await paidAmounts.GetNetPaidAsync(chargeable, cancellationToken);

        return history
            .Select(m =>
            {
                var response = MembershipMapper.ToResponse(
                    m, planNames.GetValueOrDefault(m.PlanId, "(plan eliminado)"), today, freezes,
                    m.IsPass ? used.GetValueOrDefault(m.Id) : null);
                var due = chargeable.Contains(m.Id) ? m.TotalToCharge - paid.GetValueOrDefault(m.Id) : 0m;
                return due > 0 ? response with { AmountDue = due } : response;
            })
            .ToList();
    }
}
