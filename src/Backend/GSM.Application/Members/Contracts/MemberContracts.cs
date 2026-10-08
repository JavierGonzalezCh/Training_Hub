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

public sealed record CreateMemberResponse(Guid Id);

public sealed record InviteMemberResponse(string Code, DateTimeOffset ExpiresAt);

/// <summary>
/// Canje anónimo de la invitación. El email sale de la ficha del miembro; <c>Email</c> solo se manda
/// (y se usa) si la ficha no tiene. Si el email ya tiene cuenta (es miembro de otro gym), <c>Password</c>
/// debe ser la contraseña de esa cuenta y solo se agrega el acceso a este gym.
/// </summary>
public sealed record RegisterMemberRequest(
    string Code,
    string DocumentNumber,
    string? Email,
    string Password);

/// <summary>Resultado del canje dentro de Members. El controller le suma la sesión (<see cref="RegisterMemberResponse"/>).</summary>
public sealed record RegisterMemberResult(Guid MemberId, Guid UserId, Guid GymId);

/// <summary>Respuesta del registro: el miembro queda vinculado y con la sesión iniciada.</summary>
public sealed record RegisterMemberResponse(
    Guid MemberId,
    Guid UserId,
    GSM.Application.Identity.Contracts.AuthTokensResponse Session);

public sealed record MemberListItem(
    Guid Id,
    string FirstName,
    string LastName,
    string DocumentNumber,
    string? Phone,
    string Status,
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
    DateOnly JoinedAt,
    DateTimeOffset CreatedAt)
{
    /// <summary>Membresía vigente hoy. La rellena el controller vía IMembershipReader; <c>null</c> si no hay.</summary>
    public MemberCurrentMembership? CurrentMembership { get; init; }

    /// <summary>Últimos pagos del miembro. Los rellena el controller vía IPaymentReader.</summary>
    public IReadOnlyList<MemberRecentPayment> RecentPayments { get; init; } = [];
}

public sealed record MemberCurrentMembership(
    Guid MembershipId,
    Guid PlanId,
    string PlanName,
    DateOnly StartDate,
    DateOnly EndDate);

public sealed record MemberRecentPayment(
    Guid Id,
    decimal Amount,
    string Method,
    string Concept,
    DateTimeOffset PaidAt);
