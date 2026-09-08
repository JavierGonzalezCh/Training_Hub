using GSM.Application.Common.Events;

namespace GSM.Application.Payments.Events;

public sealed record PaymentRecorded(
    Guid PaymentId, Guid GymId, Guid? MemberId, Guid? MembershipId, decimal Amount) : IDomainEvent;
