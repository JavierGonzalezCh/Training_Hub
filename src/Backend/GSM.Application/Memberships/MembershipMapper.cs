using GSM.Application.Memberships.Contracts;
using GSM.Domain.Memberships;
using Riok.Mapperly.Abstractions;

namespace GSM.Application.Memberships;

[Mapper(RequiredMappingStrategy = RequiredMappingStrategy.Target)]
public static partial class MembershipMapper
{
    public static partial PlanResponse ToResponse(MembershipPlan plan);

    public static MembershipResponse ToResponse(Membership membership, string planName, DateOnly today)
        => new(
            membership.Id,
            membership.MemberId,
            membership.PlanId,
            planName,
            membership.EffectiveStatusOn(today).ToString(),
            membership.StartDate,
            membership.EndDate,
            membership.CreatedAt);
}
