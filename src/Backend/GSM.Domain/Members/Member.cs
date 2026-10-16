using System.Globalization;
using GSM.Domain.Common;
using GSM.Domain.Common.Exceptions;

namespace GSM.Domain.Members;

/// <summary>
/// Datos editables de la ficha de un miembro (Members v2, §3.1). Sin salud, entrenador (se asigna aparte) ni
/// autorizaciones: esas piezas llegan en sub-etapas posteriores.
/// </summary>
public sealed record MemberProfile(
    string FirstName,
    string LastName,
    DocumentType DocumentType,
    string DocumentNumber,
    DateOnly? BirthDate,
    Gender? Gender = null,
    string? PhotoUrl = null,
    string? Phone = null,
    string? Email = null,
    string? Address = null,
    string? City = null,
    string? EmergencyContactName = null,
    string? EmergencyContactRelationship = null,
    string? EmergencyContactPhone = null,
    MemberSource? Source = null,
    Guid? ReferredByMemberId = null,
    MemberGoal? Goal = null,
    IReadOnlyList<string>? Tags = null,
    string? GuardianName = null,
    DocumentType? GuardianDocumentType = null,
    string? GuardianDocumentNumber = null,
    string? GuardianPhone = null,
    string? GuardianRelationship = null);

/// <summary>
/// Un campo de la ficha que cambió: nombre estable y valores en texto. <paramref name="IsSensitive"/>
/// marca los campos de salud: sus filas del historial solo las ve quien tenga el permiso de salud.
/// </summary>
public sealed record MemberFieldChange(string Field, string? OldValue, string? NewValue, bool IsSensitive = false);

/// <summary>Datos de salud de la ficha (M3). Datos sensibles (Ley 1581); se editan aparte del resto de la ficha.</summary>
public sealed record MemberHealthData(
    string? MedicalConditions,
    string? Medications,
    string? TrainingRestrictions,
    DateOnly? MedicalClearanceDate);

/// <summary>
/// Persona que asiste al gimnasio. Puede existir sin <see cref="UserId"/> (registrada en recepción,
/// aún sin cuenta en la app); el vínculo con Identity se establece después, por código de invitación.
/// </summary>
public sealed class Member : BaseEntity, ITenantEntity
{
    public const int AdultAge = 18;

    private Member()
    {
    }

    public Guid GymId { get; private set; }

    /// <summary>Cuenta de Identity vinculada. <c>null</c> hasta que el miembro canjea su invitación.</summary>
    public Guid? UserId { get; private set; }

    public string FirstName { get; private set; } = string.Empty;

    public string LastName { get; private set; } = string.Empty;

    public DocumentType DocumentType { get; private set; } = DocumentType.Cc;

    public string DocumentNumber { get; private set; } = string.Empty;

    /// <summary>
    /// Obligatoria para fichas nuevas (la exige la API); las anteriores a la v2 pueden no tenerla y
    /// quedan marcadas como incompletas (<see cref="IsProfileIncomplete"/>).
    /// </summary>
    public DateOnly? BirthDate { get; private set; }

    public Gender? Gender { get; private set; }

    public string? PhotoUrl { get; private set; }

    /// <summary>Teléfono en E.164. Un dato anterior a la v2 que no se pudo interpretar se deja tal cual.</summary>
    public string? Phone { get; private set; }

    public string? Email { get; private set; }

    public string? Address { get; private set; }

    public string? City { get; private set; }

    public string? EmergencyContactName { get; private set; }

    public string? EmergencyContactRelationship { get; private set; }

    public string? EmergencyContactPhone { get; private set; }

    public MemberSource? Source { get; private set; }

    public Guid? ReferredByMemberId { get; private set; }

    public MemberGoal? Goal { get; private set; }

    public string[] Tags { get; private set; } = [];

    /// <summary>
    /// Entrenador asignado (M1b): cuenta de Identity con el permiso <c>Trainer</c>, o el Owner. Nulo = sin
    /// entrenador. Es una referencia, no un candado, y no hay FK: Identity es otro módulo, así que el caso
    /// de uso lo valida contra <c>IIdentityReader</c>. No forma parte de <see cref="MemberProfile"/>: la
    /// edición de la ficha no lo toca.
    /// </summary>
    public Guid? TrainerUserId { get; private set; }

    /// <summary>Salud (M3): datos sensibles. Nunca viajan en el listado ni en <c>MemberDetail</c>; salen por <c>/health</c>.</summary>
    public string? MedicalConditions { get; private set; }

    public string? Medications { get; private set; }

    public string? TrainingRestrictions { get; private set; }

    /// <summary>Fecha del certificado o apto médico.</summary>
    public DateOnly? MedicalClearanceDate { get; private set; }

    /// <summary>
    /// Hay condiciones o restricciones anotadas. Es lo único de salud que ve el staff sin permiso. Las
    /// consultas lo repiten como expresión (<c>MedicalConditions != null || TrainingRestrictions != null</c>);
    /// los campos se guardan nulos cuando quedan vacíos.
    /// </summary>
    public bool HasHealthAlerts => MedicalConditions is not null || TrainingRestrictions is not null;

    public string? GuardianName { get; private set; }

    public DocumentType? GuardianDocumentType { get; private set; }

    public string? GuardianDocumentNumber { get; private set; }

    public string? GuardianPhone { get; private set; }

    public string? GuardianRelationship { get; private set; }

    public MemberStatus Status { get; private set; } = MemberStatus.Active;

    public DateOnly JoinedAt { get; private set; }

    /// <summary>
    /// Día de negocio de la baja (M6). Nulo mientras está activo. Desde aquí corre el plazo de anonimización del gym;
    /// reactivar lo borra y el plazo deja de correr.
    /// </summary>
    public DateOnly? DeactivatedOn { get; private set; }

    /// <summary>Instante en que la ficha se anonimizó (M6). Nulo = no anonimizada. Es irreversible.</summary>
    public DateTimeOffset? AnonymizedAt { get; private set; }

    /// <summary>Día en que se avisó al Owner de la anonimización próxima (una vez por baja). Nulo = sin avisar.</summary>
    public DateOnly? AnonymizationNoticeSentOn { get; private set; }

    public bool IsAnonymized => AnonymizedAt is not null;

    /// <summary>
    /// "Ficha incompleta": falta la fecha de nacimiento o el teléfono guardado no es un E.164
    /// válido. Se deriva de los datos, así que desaparece sola cuando el staff los completa.
    /// </summary>
    public bool IsProfileIncomplete => BirthDate is null || (Phone is not null && !PhoneNumber.IsE164(Phone));

    public bool IsMinor(DateOnly today) => BirthDate is { } birth && birth.AddYears(AdultAge) > today;

    /// <summary>Alta con la ficha completa. <paramref name="today"/> es el día de negocio (<c>IClock.Today</c>).</summary>
    public static Member Create(Guid gymId, MemberProfile profile, DateOnly joinedAt, DateOnly today)
    {
        if (gymId == Guid.Empty)
        {
            throw new ValidationDomainException("El miembro necesita un gym.");
        }

        var member = new Member
        {
            GymId = gymId,
            Status = MemberStatus.Active,
            JoinedAt = RequireJoinedAt(joinedAt, today),
        };
        member.Apply(profile, today);
        return member;
    }

    /// <summary>Alta mínima (documento <c>cc</c>, sin fecha de nacimiento): semillas y pruebas.</summary>
    public static Member Create(
        Guid gymId,
        string firstName,
        string lastName,
        string documentNumber,
        DateOnly joinedAt,
        string? phone = null,
        string? email = null,
        DateOnly? birthDate = null,
        string? photoUrl = null)
        => Create(
            gymId,
            new MemberProfile(
                firstName, lastName, DocumentType.Cc, documentNumber, birthDate,
                PhotoUrl: photoUrl, Phone: phone, Email: email),
            joinedAt,
            today: joinedAt);

    /// <summary>
    /// Reemplaza los datos de la ficha y devuelve un <see cref="MemberFieldChange"/> por cada campo
    /// que de verdad cambió. <paramref name="joinedAt"/> nulo = no tocar la fecha de ingreso.
    /// </summary>
    public IReadOnlyList<MemberFieldChange> UpdateProfile(MemberProfile profile, DateOnly? joinedAt, DateOnly today)
    {
        EnsureNotAnonymized();
        var before = Snapshot();

        if (BirthDate is not null && profile.BirthDate is null)
        {
            throw new ValidationDomainException("La fecha de nacimiento no se puede quitar de una ficha.");
        }

        if (profile.ReferredByMemberId == Id)
        {
            throw new ValidationDomainException("Un miembro no puede ser su propio referente.");
        }

        Apply(profile, today);
        if (joinedAt is { } value)
        {
            JoinedAt = RequireJoinedAt(value, today);
        }

        var after = Snapshot();
        var changes = new List<MemberFieldChange>();
        foreach (var (field, newValue) in after)
        {
            if (!string.Equals(before[field], newValue, StringComparison.Ordinal))
            {
                changes.Add(new MemberFieldChange(field, before[field], newValue));
            }
        }

        return changes;
    }

    /// <summary>
    /// Autoedición del miembro (M5): solo contacto y emergencia. Nombres, documento, fecha de nacimiento,
    /// email (que pasa por la verificación de Identity), foto (que se sube aparte), acudiente y los datos
    /// de gestión del staff no se tocan. Devuelve un cambio por cada campo que de verdad cambió.
    /// </summary>
    public IReadOnlyList<MemberFieldChange> UpdateContact(
        string? phone,
        string? address,
        string? city,
        string? emergencyContactName,
        string? emergencyContactRelationship,
        string? emergencyContactPhone,
        DateOnly today)
        => UpdateProfile(
            new MemberProfile(
                FirstName, LastName, DocumentType, DocumentNumber, BirthDate, Gender, PhotoUrl,
                phone, Email, address, city,
                emergencyContactName, emergencyContactRelationship, emergencyContactPhone,
                Source, ReferredByMemberId, Goal, Tags,
                GuardianName, GuardianDocumentType, GuardianDocumentNumber, GuardianPhone, GuardianRelationship),
            joinedAt: null,
            today);

    public const int HealthTextMaxLength = 1000;

    /// <summary>
    /// Reemplaza los datos de salud y devuelve un cambio sensible por cada campo que de verdad cambió.
    /// Los textos vacíos quedan nulos. La fecha del apto médico no puede ser futura.
    /// </summary>
    public IReadOnlyList<MemberFieldChange> UpdateHealth(MemberHealthData data, DateOnly today)
    {
        EnsureNotAnonymized();
        if (data.MedicalClearanceDate is { } clearance && clearance > today)
        {
            throw new ValidationDomainException("La fecha del apto médico no puede ser futura.");
        }

        var conditions = OptionalText(data.MedicalConditions, "Las condiciones médicas", HealthTextMaxLength);
        var medications = OptionalText(data.Medications, "Los medicamentos", HealthTextMaxLength);
        var restrictions = OptionalText(data.TrainingRestrictions, "Las restricciones de entrenamiento", HealthTextMaxLength);

        var changes = new List<MemberFieldChange>();
        void Track(string field, string? before, string? after)
        {
            if (!string.Equals(before, after, StringComparison.Ordinal))
            {
                changes.Add(new MemberFieldChange(field, before, after, IsSensitive: true));
            }
        }

        Track("medical_conditions", MedicalConditions, conditions);
        Track("medications", Medications, medications);
        Track("training_restrictions", TrainingRestrictions, restrictions);
        Track(
            "medical_clearance_date",
            MedicalClearanceDate?.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
            data.MedicalClearanceDate?.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture));

        MedicalConditions = conditions;
        Medications = medications;
        TrainingRestrictions = restrictions;
        MedicalClearanceDate = data.MedicalClearanceDate;
        return changes;
    }

    /// <summary>Asigna, cambia o quita (<c>null</c>) el entrenador. Devuelve si algo cambió.</summary>
    public bool AssignTrainer(Guid? trainerUserId)
    {
        EnsureNotAnonymized();
        if (trainerUserId == Guid.Empty)
        {
            throw new ValidationDomainException("El entrenador no es válido.");
        }

        if (TrainerUserId == trainerUserId)
        {
            return false;
        }

        TrainerUserId = trainerUserId;
        return true;
    }

    /// <param name="today">Día de negocio de la baja (<c>IClock.Today</c>); arranca el plazo de anonimización.</param>
    public void Deactivate(DateOnly? today = null)
    {
        if (Status == MemberStatus.Inactive)
        {
            return;
        }

        Status = MemberStatus.Inactive;
        DeactivatedOn = today;
        AnonymizationNoticeSentOn = null;
    }

    /// <summary>Día en que la ficha se anonimiza si sigue de baja, dado el plazo del gym. Nulo si no hay plazo corriendo.</summary>
    public DateOnly? AnonymizesOn(int months)
        => Status == MemberStatus.Inactive && AnonymizedAt is null && DeactivatedOn is { } on ? on.AddMonths(months) : null;

    /// <summary>El Owner ya fue avisado de la anonimización próxima.</summary>
    public void MarkAnonymizationNoticeSent(DateOnly today) => AnonymizationNoticeSentOn = today;

    /// <summary>
    /// Anonimiza la ficha (§11): vacía los datos personales y sensibles y conserva los de actividad. Irreversible;
    /// idempotente (<c>null</c> si ya estaba anonimizada). Devuelve la URL de la foto que había, para que quien llama la
    /// borre del almacenamiento. Solo se anonimiza a un miembro dado de baja.
    /// </summary>
    public string? Anonymize(DateTimeOffset now)
    {
        if (AnonymizedAt is not null)
        {
            return null;
        }

        if (Status != MemberStatus.Inactive)
        {
            throw new ConflictException("Solo se anonimiza a un miembro dado de baja.");
        }

        var photo = PhotoUrl;
        var hex = Id.ToString("N");

        FirstName = "Miembro anonimizado";
        LastName = hex[^4..];

        // 25 caracteres: el documento admite 30. Se toma la parte aleatoria del UUID v7 para no chocar con el índice único.
        DocumentNumber = "ANON-" + hex[12..32];
        BirthDate = BirthDate is { } birth ? new DateOnly(birth.Year, 1, 1) : null;
        PhotoUrl = null;
        Phone = null;
        Email = null;
        Address = null;
        City = null;
        EmergencyContactName = null;
        EmergencyContactRelationship = null;
        EmergencyContactPhone = null;
        GuardianName = null;
        GuardianDocumentType = null;
        GuardianDocumentNumber = null;
        GuardianPhone = null;
        GuardianRelationship = null;
        MedicalConditions = null;
        Medications = null;
        TrainingRestrictions = null;
        MedicalClearanceDate = null;
        TrainerUserId = null;
        UserId = null;
        AnonymizedAt = now;
        return photo;
    }

    private void EnsureNotAnonymized()
    {
        if (AnonymizedAt is not null)
        {
            throw new ConflictException("La ficha está anonimizada y no se puede modificar.");
        }
    }

    /// <summary>Vuelve a activo a un miembro dado de baja. Uno que ya está activo es un choque de estado.</summary>
    public void Reactivate()
    {
        EnsureNotAnonymized();
        if (Status == MemberStatus.Active)
        {
            throw new ConflictException("El miembro ya está activo.");
        }

        Status = MemberStatus.Active;
        DeactivatedOn = null;
        AnonymizationNoticeSentOn = null;
    }

    /// <summary>Fija solo la foto (subida de archivo).</summary>
    public void SetPhoto(string? photoUrl)
    {
        EnsureNotAnonymized();
        PhotoUrl = Clean(photoUrl);
    }

    /// <summary>Vincula la cuenta de Identity. Solo una vez (etapa 3b, canje de invitación).</summary>
    public void LinkUser(Guid userId)
    {
        EnsureNotAnonymized();
        if (userId == Guid.Empty)
        {
            throw new ValidationDomainException("userId inválido.");
        }

        if (UserId is not null)
        {
            throw new ConflictException("El miembro ya tiene una cuenta vinculada.");
        }

        UserId = userId;
    }

    /// <summary>
    /// Pone el email de la ficha (lo usa el cambio de email de la cuenta, Identity §3.4). Devuelve el cambio para el
    /// historial, o <c>null</c> si el email ya era ese (comparación sin distinguir mayúsculas): así es idempotente.
    /// </summary>
    public MemberFieldChange? ChangeEmail(string newEmail)
    {
        if (AnonymizedAt is not null)
        {
            return null;
        }

        var email = OptionalText(newEmail, "El email", 256);
        if (string.Equals(Email, email, StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        var change = new MemberFieldChange("email", Email, email);
        Email = email;
        return change;
    }

    /// <summary>Valores actuales de los campos de la ficha, en texto y con nombre estable (historial).</summary>
    public IReadOnlyDictionary<string, string?> Snapshot() => new Dictionary<string, string?>
    {
        ["first_name"] = FirstName,
        ["last_name"] = LastName,
        ["document_type"] = EnumText.Snake(DocumentType),
        ["document_number"] = DocumentNumber,
        ["birth_date"] = BirthDate?.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
        ["gender"] = EnumText.Snake(Gender),
        ["photo_url"] = PhotoUrl,
        ["phone"] = Phone,
        ["email"] = Email,
        ["address"] = Address,
        ["city"] = City,
        ["emergency_contact_name"] = EmergencyContactName,
        ["emergency_contact_relationship"] = EmergencyContactRelationship,
        ["emergency_contact_phone"] = EmergencyContactPhone,
        ["source"] = EnumText.Snake(Source),
        ["referred_by_member_id"] = ReferredByMemberId?.ToString(),
        ["goal"] = EnumText.Snake(Goal),
        ["tags"] = Tags.Length == 0 ? null : string.Join(", ", Tags),
        ["guardian_name"] = GuardianName,
        ["guardian_document_type"] = EnumText.Snake(GuardianDocumentType),
        ["guardian_document_number"] = GuardianDocumentNumber,
        ["guardian_phone"] = GuardianPhone,
        ["guardian_relationship"] = GuardianRelationship,
        ["joined_at"] = JoinedAt.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
    };

    private void Apply(MemberProfile p, DateOnly today)
    {
        if (p.BirthDate is { } birth && birth >= today)
        {
            throw new ValidationDomainException("La fecha de nacimiento debe ser pasada.");
        }

        var firstName = RequireText(p.FirstName, "El nombre", 100);
        var lastName = RequireText(p.LastName, "El apellido", 100);
        var documentNumber = RequireText(p.DocumentNumber, "El documento", 30);

        var phone = ResolvePhone(p.Phone, Phone, "El teléfono");
        var emergencyPhone = ResolvePhone(p.EmergencyContactPhone, EmergencyContactPhone, "El teléfono del contacto de emergencia");
        var guardianPhone = ResolvePhone(p.GuardianPhone, GuardianPhone, "El teléfono del acudiente");

        var guardianName = OptionalText(p.GuardianName, "El nombre del acudiente", 150);
        var guardianDocumentNumber = OptionalText(p.GuardianDocumentNumber, "El documento del acudiente", 30);
        var guardianRelationship = OptionalText(p.GuardianRelationship, "El parentesco del acudiente", 50);

        if (p.BirthDate is { } b && b.AddYears(AdultAge) > today
            && (guardianName is null || p.GuardianDocumentType is null || guardianDocumentNumber is null
                || guardianPhone is null || guardianRelationship is null))
        {
            throw new ValidationDomainException(
                "El miembro es menor de edad: el nombre, tipo y número de documento, teléfono y parentesco del acudiente son obligatorios.");
        }

        FirstName = firstName;
        LastName = lastName;
        DocumentType = p.DocumentType;
        DocumentNumber = documentNumber;
        BirthDate = p.BirthDate;
        Gender = p.Gender;
        PhotoUrl = OptionalText(p.PhotoUrl, "La foto", 2048);
        Phone = phone;
        Email = OptionalText(p.Email, "El email", 256);
        Address = OptionalText(p.Address, "La dirección", 200);
        City = OptionalText(p.City, "La ciudad", 100);
        EmergencyContactName = OptionalText(p.EmergencyContactName, "El nombre del contacto de emergencia", 150);
        EmergencyContactRelationship = OptionalText(p.EmergencyContactRelationship, "El parentesco del contacto de emergencia", 50);
        EmergencyContactPhone = emergencyPhone;
        Source = p.Source;
        ReferredByMemberId = p.ReferredByMemberId;
        Goal = p.Goal;
        Tags = NormalizeTags(p.Tags);
        GuardianName = guardianName;
        GuardianDocumentType = p.GuardianDocumentType;
        GuardianDocumentNumber = guardianDocumentNumber;
        GuardianPhone = guardianPhone;
        GuardianRelationship = guardianRelationship;
    }

    /// <summary>
    /// Normaliza a E.164. Si el valor llega igual al guardado se respeta tal cual: así un dato
    /// anterior a la v2 que no se pudo interpretar no bloquea editar el resto de la ficha.
    /// </summary>
    private static string? ResolvePhone(string? input, string? current, string label)
    {
        var trimmed = Clean(input);
        if (trimmed is null)
        {
            return null;
        }

        if (trimmed == current)
        {
            return current;
        }

        return PhoneNumber.TryNormalize(trimmed)
            ?? throw new ValidationDomainException(
                $"{label} no es válido. Usa el formato internacional (+573001234567) o 10 dígitos de Colombia.");
    }

    private static string[] NormalizeTags(IReadOnlyList<string>? tags)
    {
        if (tags is null)
        {
            return [];
        }

        var result = new List<string>();
        foreach (var raw in tags)
        {
            var tag = Clean(raw);
            if (tag is null)
            {
                continue;
            }

            if (tag.Length > 40)
            {
                throw new ValidationDomainException("Cada etiqueta puede tener hasta 40 caracteres.");
            }

            if (!result.Contains(tag, StringComparer.OrdinalIgnoreCase))
            {
                result.Add(tag);
            }
        }

        if (result.Count > 20)
        {
            throw new ValidationDomainException("Un miembro puede tener hasta 20 etiquetas.");
        }

        return [.. result];
    }

    private static DateOnly RequireJoinedAt(DateOnly joinedAt, DateOnly today)
    {
        if (joinedAt > today)
        {
            throw new ValidationDomainException("La fecha de ingreso no puede ser futura.");
        }

        return joinedAt;
    }

    private static string RequireText(string value, string label, int maxLength)
    {
        var trimmed = (value ?? string.Empty).Trim();
        if (trimmed.Length == 0 || trimmed.Length > maxLength)
        {
            throw new ValidationDomainException($"{label} es obligatorio y no puede exceder {maxLength} caracteres.");
        }

        return trimmed;
    }

    private static string? OptionalText(string? value, string label, int maxLength)
    {
        var cleaned = Clean(value);
        if (cleaned is not null && cleaned.Length > maxLength)
        {
            throw new ValidationDomainException($"{label} no puede exceder {maxLength} caracteres.");
        }

        return cleaned;
    }

    private static string? Clean(string? value)
        => string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}
