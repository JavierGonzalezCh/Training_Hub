using GSM.Application.Common.Events;

namespace GSM.Application.Memberships.Events;

public sealed record MembershipCreated(
    Guid MembershipId, Guid MemberId, Guid GymId, Guid PlanId) : IDomainEvent;

/// <summary>La membresía nueva se encadenó tras otra vigente.</summary>
public sealed record MembershipRenewed(
    Guid MembershipId, Guid MemberId, Guid GymId, Guid PlanId, Guid PreviousMembershipId) : IDomainEvent;

/// <summary>MS5-5: un check-in gastó la última entrada de una tiquetera (le quedan 0).</summary>
public sealed record PassExhausted(Guid MembershipId, Guid MemberId, Guid GymId) : IDomainEvent;

public sealed record MembershipCancelled(Guid MembershipId, Guid MemberId, Guid GymId) : IDomainEvent;

/// <summary>
/// Cambio de plan (MS6): <paramref name="PreviousMembershipId"/> se canceló y <paramref name="MembershipId"/> la reemplaza.
/// Se publica además de <see cref="MembershipCancelled"/> y <see cref="MembershipCreated"/>.
/// </summary>
public sealed record MembershipPlanChanged(
    Guid MembershipId, Guid PreviousMembershipId, Guid MemberId, Guid GymId, Guid PlanId, decimal CreditApplied,
    decimal RefundAmount) : IDomainEvent;

/// <summary>Se creó un congelamiento (abierto desde hoy o programado). <paramref name="EndsOn"/> nulo = abierto.</summary>
public sealed record MembershipFrozen(
    Guid MembershipId, Guid MemberId, Guid GymId, DateOnly StartsOn, DateOnly? EndsOn, int DaysAdded) : IDomainEvent;

/// <summary>Se deshizo un congelamiento programado que aún no empezaba.</summary>
public sealed record MembershipFreezeRemoved(Guid MembershipId, Guid MemberId, Guid GymId, int DaysReturned) : IDomainEvent;

/// <summary>La membresía se reanudó y su fin (y el de las encadenadas) se corrió <paramref name="DaysAdded"/> días.</summary>
public sealed record MembershipResumed(Guid MembershipId, Guid MemberId, Guid GymId, int DaysAdded) : IDomainEvent;
