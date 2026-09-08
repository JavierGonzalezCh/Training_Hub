using GSM.Application.Common.Events;

namespace GSM.Application.Memberships.Events;

public sealed record MembershipCreated(
    Guid MembershipId, Guid MemberId, Guid GymId, Guid PlanId) : IDomainEvent;

/// <summary>La membresía nueva se encadenó tras otra vigente.</summary>
public sealed record MembershipRenewed(
    Guid MembershipId, Guid MemberId, Guid GymId, Guid PlanId, Guid PreviousMembershipId) : IDomainEvent;
