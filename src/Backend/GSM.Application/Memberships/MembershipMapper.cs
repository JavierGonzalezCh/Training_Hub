using GSM.Application.Memberships.Contracts;
using GSM.Application.Memberships.Public;
using GSM.Domain.Members;
using GSM.Domain.Memberships;
using Riok.Mapperly.Abstractions;

namespace GSM.Application.Memberships;

[Mapper(RequiredMappingStrategy = RequiredMappingStrategy.Target)]
public static partial class MembershipMapper
{
    public static PlanResponse ToResponse(MembershipPlan plan)
        => new(
            plan.Id,
            plan.Name,
            plan.Description,
            plan.Price,
            EnumText.Snake(plan.DurationUnit)!,
            plan.DurationValue,
            plan.IsActive,
            plan.CreatedAt,
            plan.MaxFreezeDays,
            plan.MinFreezeDays);

    public static MembershipFreezeResponse ToResponse(MembershipFreeze freeze, DateOnly today)
        => new(
            freeze.Id,
            freeze.MembershipId,
            freeze.StartsOn,
            freeze.EndsOn,
            freeze.Reason,
            freeze.OverrideLimit,
            freeze.DaysCredited,
            EnumText.Snake(freeze.StateOn(today))!,
            freeze.CreatedAt,
            freeze.CreatedBy);

    public static MembershipResponse ToResponse(
        Membership membership, string planName, DateOnly today, IEnumerable<MembershipFreeze>? freezes = null)
    {
        var own = (freezes ?? []).Where(f => f.MembershipId == membership.Id).ToList();
        var active = membership.Status == MembershipStatus.Active
            ? own.FirstOrDefault(f => f.CoversDate(today))
            : null;
        var next = membership.Status == MembershipStatus.Active
            ? own.Where(f => f.StartsOn > today).OrderBy(f => f.StartsOn).FirstOrDefault()
            : null;
        return Build(membership, planName, today, active, next);
    }

    private static MembershipResponse Build(
        Membership membership, string planName, DateOnly today, MembershipFreeze? active, MembershipFreeze? next)
        => new(
            membership.Id,
            membership.MemberId,
            membership.PlanId,
            planName,
            membership.EffectiveStatusOn(today, active is not null).ToString(),
            membership.StartDate,
            membership.EndDate,
            membership.CreatedAt,
            membership.CancellationReason,
            EnumText.Snake(membership.RenewalKind)!,
            membership.StartOverrideReason,
            membership.AgreedPrice,
            membership.DiscountReason,
            membership.EnrollmentFee,
            membership.EnrollmentWaivedReason,
            membership.TotalToCharge,
            membership.CycleDay,
            active is null ? null : ToResponse(active, today),
            next is null ? null : ToResponse(next, today));

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
