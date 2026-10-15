using GSM.Application.Memberships.Contracts;
using GSM.Application.Memberships.Public;
using GSM.Domain.Members;
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
            membership.CreatedAt,
            membership.PausedOn,
            membership.CancellationReason,
            EnumText.Snake(membership.RenewalKind)!,
            membership.StartOverrideReason);

    public static AccessStatusResponse ToResponse(AccessStatus access)
        => new(
            EnumText.Snake(access.Commercial)!,
            EnumText.Snake(access.Kind)!,
            EnumText.Snake(access.BlockReason),
            access.CanEnter,
            access.EndsOn,
            access.DaysLeft,
            access.GraceEndsOn,
            access.GraceDaysLeft,
            access.Notice,
            access.BlockMessage,
            access.StartsOn);
}
