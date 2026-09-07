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
    DateTimeOffset CreatedAt);
