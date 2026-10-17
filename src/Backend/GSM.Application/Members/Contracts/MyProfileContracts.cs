namespace GSM.Application.Members.Contracts;

/// <summary>
/// "Mi perfil" (M5): la ficha del propio miembro. Nunca trae las notas internas, el origen, las etiquetas
/// ni el referente (son del staff). Los enums viajan como texto snake_case. La salud es suya y la ve, pero
/// con el mismo candado que la del staff: sin la autorización de tratamiento de datos (aceptada y no
/// revocada) llega bloqueada (<c>HealthConsentRequired</c>, sin <c>Health</c>). Consultarla no queda en el
/// registro de consultas de salud (§10: no se registra cuando el propio miembro mira su ficha).
/// </summary>
/// <param name="Email">Solo lectura aquí: se cambia con la verificación del correo nuevo (<c>/api/me/request-email-change</c>).</param>
/// <param name="CanEditHealth">Falso si es menor de edad o si no hay autorización de datos. Aplica a salud y mediciones.</param>
public sealed record MyProfile(
    Guid Id,
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
    string? Goal,
    string? GuardianName,
    string? GuardianPhone,
    string? GuardianRelationship,
    DateOnly JoinedAt,
    bool IsMinor,
    bool IsProfileIncomplete,
    Guid? TrainerUserId,
    string? TrainerName,
    bool HealthConsentRequired,
    bool CanEditHealth,
    MemberHealth? Health);

/// <summary>
/// Autoedición de la ficha (M5). Reemplaza contacto y emergencia (lo vacío los deja sin dato). <c>Health</c>
/// nulo = no tocar la salud; si viene, reemplaza la sección completa (409 sin autorización de datos, 403
/// siendo menor de edad). Los campos de identidad (<c>FirstName</c>, <c>LastName</c>, <c>DocumentType</c>,
/// <c>DocumentNumber</c>, <c>BirthDate</c>) existen para que un cliente pueda reenviar la ficha completa: si
/// vienen con un valor distinto al guardado → 403 (solo el staff los cambia). Un <c>Email</c> distinto al
/// guardado → 422: se cambia con la verificación del correo nuevo. <c>Goal</c> (objetivo, texto snake_case) se reemplaza como el contacto: vacío lo quita; el género no lo edita el miembro. La foto se sube por <c>/api/me/photo</c>.
/// </summary>
public sealed record UpdateMyProfileRequest(
    string? Phone,
    string? Address,
    string? City,
    string? EmergencyContactName,
    string? EmergencyContactRelationship,
    string? EmergencyContactPhone,
    UpdateMemberHealthRequest? Health,
    string? FirstName,
    string? LastName,
    string? DocumentType,
    string? DocumentNumber,
    DateOnly? BirthDate,
    string? Email,
    string? Goal = null);
