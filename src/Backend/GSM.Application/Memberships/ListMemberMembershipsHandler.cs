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
    IMembershipRepository memberships)
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

        return history
            .Select(m => MembershipMapper.ToResponse(
                m, planNames.GetValueOrDefault(m.PlanId, "(plan eliminado)"), today))
            .ToList();
    }
}
