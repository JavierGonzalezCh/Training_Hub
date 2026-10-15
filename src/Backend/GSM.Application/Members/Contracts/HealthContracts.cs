namespace GSM.Application.Members.Contracts;

/// <summary>Datos de salud de un miembro (M3). Sensibles: nunca se loguean ni viajan en eventos.</summary>
public sealed record MemberHealth(
    string? MedicalConditions,
    string? Medications,
    string? TrainingRestrictions,
    DateOnly? MedicalClearanceDate);

/// <summary>Reemplaza los datos de salud completos; los textos vacíos los dejan sin dato.</summary>
public sealed record UpdateMemberHealthRequest(
    string? MedicalConditions,
    string? Medications,
    string? TrainingRestrictions,
    DateOnly? MedicalClearanceDate);

/// <summary>
/// Sección de salud de la ficha. Si el miembro no tiene la autorización de tratamiento de datos
/// (aceptada y no revocada), la sección está bloqueada: <c>ConsentRequired</c> y sin <c>Health</c>.
/// </summary>
public sealed record MemberHealthView(bool ConsentRequired, MemberHealth? Health);

/// <summary>Una consulta de los datos de salud: quién y cuándo (solo lo ve el Owner).</summary>
public sealed record MemberHealthAccessItem(
    Guid Id,
    Guid UserId,
    string? UserName,
    string Role,
    DateTimeOffset AccessedAt);
