using GSM.Domain.Members;

namespace GSM.Application.Members.Contracts;

/// <summary>
/// Datos de la ficha que comparten el alta y la edición. Los enums viajan como texto snake_case
/// (<c>cc</c>, <c>walk_in</c>, <c>weight_loss</c>…): así el cliente generado los recibe como
/// <c>string</c> y no como <c>int</c> (defecto de NSwag, ver CLAUDE.md).
/// </summary>
public interface IMemberProfileInput
{
    string FirstName { get; }
    string LastName { get; }
    string? DocumentType { get; }
    string DocumentNumber { get; }
    DateOnly? BirthDate { get; }
    string? Gender { get; }
    string? PhotoUrl { get; }
    string? Phone { get; }
    string? Email { get; }
    string? Address { get; }
    string? City { get; }
    string? EmergencyContactName { get; }
    string? EmergencyContactRelationship { get; }
    string? EmergencyContactPhone { get; }
    string? Source { get; }
    Guid? ReferredByMemberId { get; }
    string? Goal { get; }
    IReadOnlyList<string>? Tags { get; }
    string? GuardianName { get; }
    string? GuardianDocumentType { get; }
    string? GuardianDocumentNumber { get; }
    string? GuardianPhone { get; }
    string? GuardianRelationship { get; }

    /// <summary>Fecha de ingreso. Nulo = hoy en el alta, y sin cambio en la edición.</summary>
    DateOnly? JoinedAt { get; }
}

/// <summary>Alta de un miembro: <c>BirthDate</c> es obligatoria.</summary>
public sealed record CreateMemberRequest(
    string FirstName,
    string LastName,
    string? DocumentType,
    string DocumentNumber,
    DateOnly? BirthDate,
    string? Gender,
    string? PhotoUrl,
    string? Phone,
    string? Email,
    string? Address,
    string? City,
    string? EmergencyContactName,
    string? EmergencyContactRelationship,
    string? EmergencyContactPhone,
    string? Source,
    Guid? ReferredByMemberId,
    string? Goal,
    IReadOnlyList<string>? Tags,
    string? GuardianName,
    string? GuardianDocumentType,
    string? GuardianDocumentNumber,
    string? GuardianPhone,
    string? GuardianRelationship,
    DateOnly? JoinedAt) : IMemberProfileInput;

/// <summary>Edición: reemplaza todos los datos de la ficha (los campos que no cambian se reenvían).</summary>
public sealed record UpdateMemberRequest(
    string FirstName,
    string LastName,
    string? DocumentType,
    string DocumentNumber,
    DateOnly? BirthDate,
    string? Gender,
    string? PhotoUrl,
    string? Phone,
    string? Email,
    string? Address,
    string? City,
    string? EmergencyContactName,
    string? EmergencyContactRelationship,
    string? EmergencyContactPhone,
    string? Source,
    Guid? ReferredByMemberId,
    string? Goal,
    IReadOnlyList<string>? Tags,
    string? GuardianName,
    string? GuardianDocumentType,
    string? GuardianDocumentNumber,
    string? GuardianPhone,
    string? GuardianRelationship,
    DateOnly? JoinedAt) : IMemberProfileInput;

/// <summary>Filtros del listado, enlazados desde la query string.</summary>
public sealed record ListMembersQuery
{
    public string? Search { get; init; }
    public MemberStatus? Status { get; init; }

    /// <summary>Solo los miembros que tienen esta etiqueta.</summary>
    public string? Tag { get; init; }

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
    string DocumentType,
    string DocumentNumber,
    string? Phone,
    string Status,
    DateOnly JoinedAt,
    IReadOnlyList<string> Tags,
    bool IsProfileIncomplete);

public sealed record MemberDetail(
    Guid Id,
    Guid GymId,
    Guid? UserId,
    string FirstName,
    string LastName,
    string DocumentType,
    string DocumentNumber,
    DateOnly? BirthDate,
    string? Gender,
    string? PhotoUrl,
    string? Phone,
    string? Email,
    string? Address,
    string? City,
    string? EmergencyContactName,
    string? EmergencyContactRelationship,
    string? EmergencyContactPhone,
    string? Source,
    Guid? ReferredByMemberId,
    string? Goal,
    IReadOnlyList<string> Tags,
    string? GuardianName,
    string? GuardianDocumentType,
    string? GuardianDocumentNumber,
    string? GuardianPhone,
    string? GuardianRelationship,
    string Status,
    DateOnly JoinedAt,
    DateTimeOffset CreatedAt,
    bool IsMinor,
    bool IsProfileIncomplete)
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

/// <summary>Una fila del historial de cambios de la ficha.</summary>
public sealed record MemberChangeItem(
    Guid Id,
    string Field,
    string? OldValue,
    string? NewValue,
    Guid? ChangedByUserId,
    string? ChangedByName,
    string ChangedByRole,
    string Source,
    DateTimeOffset ChangedAt,
    bool IsSensitive);

public sealed record ListMemberChangesQuery
{
    public int Page { get; init; } = 1;
    public int PageSize { get; init; } = 20;
}

public sealed record AddMemberNoteRequest(string Text);

public sealed record MemberNoteItem(
    Guid Id,
    string Text,
    Guid? AuthorUserId,
    string? AuthorName,
    DateTimeOffset CreatedAt);
