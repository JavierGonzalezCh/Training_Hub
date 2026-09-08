using GSM.Application.Common.Events;

namespace GSM.Application.Members.Events;

public sealed record MemberCreated(Guid MemberId, Guid GymId) : IDomainEvent;

public sealed record MemberDeactivated(Guid MemberId, Guid GymId) : IDomainEvent;

/// <summary>Se emitió un código de invitación y el miembro tiene email en la ficha.</summary>
public sealed record MemberInvited(Guid MemberId, Guid GymId, string Email, string Code) : IDomainEvent;

/// <summary>El miembro canjeó su invitación y ya tiene cuenta en la app.</summary>
public sealed record MemberRegistered(
    Guid MemberId, Guid UserId, Guid GymId, string Email, string FullName) : IDomainEvent;
