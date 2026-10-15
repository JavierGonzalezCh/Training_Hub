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

/// <summary>
/// Alta de una medición (M4). <c>MeasuredOn</c> nulo = hoy en la zona del gym. <c>HeightCm</c> nulo = vale la
/// última altura registrada. Rangos: peso 20–400 kg, altura 50–250 cm (fuera → 422).
/// </summary>
public sealed record AddMemberMeasurementRequest(DateOnly? MeasuredOn, decimal WeightKg, decimal? HeightCm);

/// <summary>
/// Una medición con su IMC calculado. <c>EffectiveHeightCm</c> es la altura propia o, si falta, la última
/// registrada hasta esa fecha; con ella se calcula <c>Bmi</c> (nulo si nunca hubo altura).
/// </summary>
public sealed record MemberMeasurementItem(
    Guid Id,
    DateOnly MeasuredOn,
    decimal WeightKg,
    decimal? HeightCm,
    decimal? EffectiveHeightCm,
    decimal? Bmi,
    Guid? RecordedByUserId,
    string? RecordedByName,
    string RecordedByRole,
    DateTimeOffset CreatedAt);

/// <summary>
/// Mediciones del miembro, de la más reciente a la más antigua. Sin la autorización de datos del miembro
/// están bloqueadas: <c>ConsentRequired</c> y lista vacía.
/// </summary>
public sealed record MemberMeasurementsView(bool ConsentRequired, IReadOnlyList<MemberMeasurementItem> Items);
