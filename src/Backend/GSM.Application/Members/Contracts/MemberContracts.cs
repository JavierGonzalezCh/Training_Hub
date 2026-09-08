using GSM.Domain.Members;

namespace GSM.Application.Members.Contracts;

public sealed record CreateMemberRequest(
    string FirstName,
    string LastName,
    string DocumentNumber,
    string? Phone,
    string? Email,
    DateOnly? BirthDate,
    string? EmergencyContact,
    string? PhotoUrl);

public sealed record UpdateMemberRequest(
    string FirstName,
    string LastName,
    string DocumentNumber,
    string? Phone,
    string? Email,
    DateOnly? BirthDate,
    string? EmergencyContact,
    string? PhotoUrl);

/// <summary>Filtros del listado, enlazados desde la query string.</summary>
public sealed record ListMembersQuery
{
    public string? Search { get; init; }
    public MemberStatus? Status { get; init; }
    public int Page { get; init; } = 1;
    public int PageSize { get; init; } = 20;
}

public sealed record CreateMemberResponse(Guid Id, string QrCode);

public sealed record InviteMemberResponse(string Code, DateTimeOffset ExpiresAt);

/// <summary>Canje anónimo de la invitación: el miembro crea su cuenta de la app.</summary>
public sealed record RegisterMemberRequest(
    string Code,
    string DocumentNumber,
    string Email,
    string Password);

public sealed record RegisterMemberResponse(Guid MemberId, Guid UserId);

public sealed record MemberListItem(
    Guid Id,
    string FirstName,
    string LastName,
    string DocumentNumber,
    string? Phone,
    string Status,
    string QrCode,
    DateOnly JoinedAt);

public sealed record MemberDetail(
    Guid Id,
    Guid GymId,
    Guid? UserId,
    string FirstName,
    string LastName,
    string DocumentNumber,
    string? Phone,
    string? Email,
    DateOnly? BirthDate,
    string? PhotoUrl,
    string? EmergencyContact,
    string Status,
    string QrCode,
    DateOnly JoinedAt,
    DateTimeOffset CreatedAt)
{
    /// <summary>Membresía vigente hoy. La rellena el controller vía IMembershipReader; <c>null</c> si no hay.</summary>
    public MemberCurrentMembership? CurrentMembership { get; init; }
}

public sealed record MemberCurrentMembership(
    Guid MembershipId,
    Guid PlanId,
    string PlanName,
    DateOnly StartDate,
    DateOnly EndDate);
