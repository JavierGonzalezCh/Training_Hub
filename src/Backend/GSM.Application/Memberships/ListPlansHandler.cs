using GSM.Application.Common.Abstractions;
using GSM.Application.Memberships.Abstractions;
using GSM.Application.Memberships.Contracts;

namespace GSM.Application.Memberships;

public sealed class ListPlansHandler(ICurrentUserService currentUser, IMembershipRepository memberships)
{
    public async Task<IReadOnlyList<PlanResponse>> HandleAsync(
        bool includeInactive, CancellationToken cancellationToken)
    {
        _ = currentUser.RequireGymId();

        var plans = await memberships.ListPlansAsync(includeInactive, cancellationToken);
        return plans.Select(MembershipMapper.ToResponse).ToList();
    }
}

public sealed class GetPlanHandler(ICurrentUserService currentUser, IMembershipRepository memberships)
{
    public async Task<PlanResponse> HandleAsync(Guid planId, CancellationToken cancellationToken)
    {
        _ = currentUser.RequireGymId();

        var plan = await memberships.GetPlanByIdAsync(planId, cancellationToken)
            ?? throw new GSM.Domain.Common.Exceptions.NotFoundException("Plan no encontrado.");

        return MembershipMapper.ToResponse(plan);
    }
}
