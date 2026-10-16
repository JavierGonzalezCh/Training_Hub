using GSM.Application.Common.Events;

namespace GSM.Application.Members.Events;

public sealed record MemberCreated(Guid MemberId, Guid GymId) : IDomainEvent;

public sealed record MemberDeactivated(Guid MemberId, Guid GymId) : IDomainEvent;

/// <summary>Se emitió un código de invitación y el miembro tiene email en la ficha.</summary>
public sealed record MemberInvited(Guid MemberId, Guid GymId, string Email, string Code) : IDomainEvent;

/// <summary>El miembro canjeó su invitación y ya tiene cuenta en la app.</summary>
public sealed record MemberRegistered(
    Guid MemberId, Guid UserId, Guid GymId, string Email, string FullName) : IDomainEvent;

/// <summary>
/// La ficha se anonimizó (M6, Members §11). Cada módulo que guarde texto libre o datos personales del miembro lo
/// limpia en su propio manejador, que debe ser idempotente (el evento puede reintentarse). <paramref name="UserId"/>
/// es la cuenta que tenía vinculada (Identity le quita el acceso de miembro a este gym); nulo si no tenía.
/// </summary>
public sealed record MemberAnonymized(Guid MemberId, Guid GymId, Guid? UserId) : IDomainEvent;

/// <summary>
/// Faltan pocos días para anonimizar la ficha de un miembro dado de baja (M6): Notifications avisa a los Owners.
/// Sin nombre a propósito: el aviso se conserva después y no debe guardar datos personales.
/// </summary>
public sealed record MemberAnonymizationDue(Guid MemberId, Guid GymId, DateOnly AnonymizesOn) : IDomainEvent;
