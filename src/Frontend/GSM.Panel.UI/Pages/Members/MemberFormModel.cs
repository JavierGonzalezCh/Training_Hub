using GSM.Client.Generated;
using GSM.UI.Shared.Formatting;

namespace GSM.Panel.UI.Pages.Members;

/// <summary>
/// Estado editable del formulario de alta y edición de un miembro. Vive aparte del componente para
/// poder probar sus reglas (menor de edad → acudiente obligatorio) sin renderizar nada.
/// </summary>
public sealed class MemberFormModel
{
    public string FirstName { get; set; } = string.Empty;
    public string LastName { get; set; } = string.Empty;
    public string DocumentType { get; set; } = "cc";
    public string DocumentNumber { get; set; } = string.Empty;
    public DateTime? BirthDate { get; set; }
    public string? Gender { get; set; }
    public string? Phone { get; set; }
    public string? Email { get; set; }
    public string? Address { get; set; }
    public string? City { get; set; }
    public string? EmergencyContactName { get; set; }
    public string? EmergencyContactRelationship { get; set; }
    public string? EmergencyContactPhone { get; set; }
    public string? Source { get; set; }
    public string? Goal { get; set; }
    public Guid? ReferredByMemberId { get; set; }
    public string? ReferredByName { get; set; }
    public string? TagsText { get; set; }
    public string? GuardianName { get; set; }
    public string? GuardianDocumentType { get; set; } = "cc";
    public string? GuardianDocumentNumber { get; set; }
    public string? GuardianPhone { get; set; }
    public string? GuardianRelationship { get; set; }
    public DateTime? JoinedAt { get; set; }

    /// <summary>La foto no se edita en este formulario (se sube desde la ficha); se conserva tal cual.</summary>
    public string? PhotoUrl { get; set; }

    public DateOnly? BirthDateOnly => BirthDate is { } d ? DateOnly.FromDateTime(d) : null;

    public bool IsMinor(DateOnly today) => MemberLabels.IsMinor(BirthDateOnly, today);

    /// <summary>Datos del acudiente que faltan (vacío si no hace falta o si están todos).</summary>
    public IReadOnlyList<string> MissingGuardianFields(DateOnly today)
    {
        if (!IsMinor(today))
        {
            return [];
        }

        var missing = new List<string>();
        if (string.IsNullOrWhiteSpace(GuardianName))
        {
            missing.Add("nombre");
        }

        if (string.IsNullOrWhiteSpace(GuardianDocumentType))
        {
            missing.Add("tipo de documento");
        }

        if (string.IsNullOrWhiteSpace(GuardianDocumentNumber))
        {
            missing.Add("número de documento");
        }

        if (string.IsNullOrWhiteSpace(GuardianPhone))
        {
            missing.Add("teléfono");
        }

        if (string.IsNullOrWhiteSpace(GuardianRelationship))
        {
            missing.Add("parentesco");
        }

        return missing;
    }

    public IReadOnlyList<string> Tags
        => (TagsText ?? string.Empty)
            .Split([',', ';'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .ToList();

    public static MemberFormModel From(MemberDetail m) => new()
    {
        FirstName = m.FirstName,
        LastName = m.LastName,
        DocumentType = m.DocumentType,
        DocumentNumber = m.DocumentNumber,
        BirthDate = m.BirthDate is { } b ? b.ToDateTime(TimeOnly.MinValue) : null,
        Gender = m.Gender,
        Phone = m.Phone,
        Email = m.Email,
        Address = m.Address,
        City = m.City,
        EmergencyContactName = m.EmergencyContactName,
        EmergencyContactRelationship = m.EmergencyContactRelationship,
        EmergencyContactPhone = m.EmergencyContactPhone,
        Source = m.Source,
        Goal = m.Goal,
        ReferredByMemberId = m.ReferredByMemberId,
        TagsText = string.Join(", ", m.Tags),
        GuardianName = m.GuardianName,
        GuardianDocumentType = m.GuardianDocumentType ?? "cc",
        GuardianDocumentNumber = m.GuardianDocumentNumber,
        GuardianPhone = m.GuardianPhone,
        GuardianRelationship = m.GuardianRelationship,
        JoinedAt = m.JoinedAt.ToDateTime(TimeOnly.MinValue),
        PhotoUrl = m.PhotoUrl,
    };

    public CreateMemberRequest ToCreateRequest() => new()
    {
        FirstName = FirstName,
        LastName = LastName,
        DocumentType = DocumentType,
        DocumentNumber = DocumentNumber,
        BirthDate = BirthDateOnly,
        Gender = Blank(Gender),
        PhotoUrl = PhotoUrl,
        Phone = Blank(Phone),
        Email = Blank(Email),
        Address = Blank(Address),
        City = Blank(City),
        EmergencyContactName = Blank(EmergencyContactName),
        EmergencyContactRelationship = Blank(EmergencyContactRelationship),
        EmergencyContactPhone = Blank(EmergencyContactPhone),
        Source = Blank(Source),
        ReferredByMemberId = ReferredByMemberId,
        Goal = Blank(Goal),
        Tags = Tags.ToList(),
        GuardianName = Blank(GuardianName),
        GuardianDocumentType = Blank(GuardianName) is null && Blank(GuardianDocumentNumber) is null ? null : Blank(GuardianDocumentType),
        GuardianDocumentNumber = Blank(GuardianDocumentNumber),
        GuardianPhone = Blank(GuardianPhone),
        GuardianRelationship = Blank(GuardianRelationship),
        JoinedAt = JoinedAt is { } j ? DateOnly.FromDateTime(j) : null,
    };

    public UpdateMemberRequest ToUpdateRequest()
    {
        var create = ToCreateRequest();
        return new UpdateMemberRequest
        {
            FirstName = create.FirstName,
            LastName = create.LastName,
            DocumentType = create.DocumentType,
            DocumentNumber = create.DocumentNumber,
            BirthDate = create.BirthDate,
            Gender = create.Gender,
            PhotoUrl = create.PhotoUrl,
            Phone = create.Phone,
            Email = create.Email,
            Address = create.Address,
            City = create.City,
            EmergencyContactName = create.EmergencyContactName,
            EmergencyContactRelationship = create.EmergencyContactRelationship,
            EmergencyContactPhone = create.EmergencyContactPhone,
            Source = create.Source,
            ReferredByMemberId = create.ReferredByMemberId,
            Goal = create.Goal,
            Tags = create.Tags,
            GuardianName = create.GuardianName,
            GuardianDocumentType = create.GuardianDocumentType,
            GuardianDocumentNumber = create.GuardianDocumentNumber,
            GuardianPhone = create.GuardianPhone,
            GuardianRelationship = create.GuardianRelationship,
            JoinedAt = create.JoinedAt,
        };
    }

    private static string? Blank(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}
