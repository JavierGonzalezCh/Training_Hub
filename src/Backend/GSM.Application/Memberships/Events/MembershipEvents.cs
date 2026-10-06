using GSM.Application.Common.Events;

namespace GSM.Application.Memberships.Events;

public sealed record MembershipCreated(
    Guid MembershipId, Guid MemberId, Guid GymId, Guid PlanId) : IDomainEvent;

/// <summary>La membresía nueva se encadenó tras otra vigente.</summary>
public sealed record MembershipRenewed(
    Guid MembershipId, Guid MemberId, Guid GymId, Guid PlanId, Guid PreviousMembershipId) : IDomainEvent;

public sealed record MembershipCancelled(Guid MembershipId, Guid MemberId, Guid GymId) : IDomainEvent;

public sealed record MembershipPaused(Guid MembershipId, Guid MemberId, Guid GymId, DateOnly PausedOn) : IDomainEvent;

/// <summary>La membresía se reanudó y su fin (y el de las encadenadas) se corrió <paramref name="DaysAdded"/> días.</summary>
public sealed record MembershipResumed(Guid MembershipId, Guid MemberId, Guid GymId, int DaysAdded) : IDomainEvent;
