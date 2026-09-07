using GSM.Application.Common.Events;

namespace GSM.Application.Members.Events;

public sealed record MemberCreated(Guid MemberId, Guid GymId) : IDomainEvent;

public sealed record MemberDeactivated(Guid MemberId, Guid GymId) : IDomainEvent;
