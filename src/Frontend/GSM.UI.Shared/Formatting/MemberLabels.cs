using System.Text.RegularExpressions;

namespace GSM.UI.Shared.Formatting;

/// <summary>
/// Textos en español de los enums de la ficha del miembro (que la API entrega como snake_case:
/// <c>walk_in</c>, <c>weight_loss</c>…), los nombres de campo del historial de cambios y las reglas
/// de "ficha incompleta" y "menor de edad" que la UI repite para avisar antes de enviar. La fuente de
/// verdad sigue siendo el servidor.
/// </summary>
public static partial class MemberLabels
{
    public const int AdultAge = 18;

    public static readonly (string Value, string Label)[] DocumentTypes =
    [
        ("cc", "Cédula de ciudadanía"),
        ("ti", "Tarjeta de identidad"),
        ("ce", "Cédula de extranjería"),
        ("pa", "Pasaporte"),
        ("ppt", "Permiso por protección temporal"),
        ("nit", "NIT"),
        ("other", "Otro"),
    ];

    public static readonly (string Value, string Label)[] Genders =
    [
        ("female", "Femenino"),
        ("male", "Masculino"),
        ("other", "Otro"),
        ("undisclosed", "Prefiere no decirlo"),
    ];

    public static readonly (string Value, string Label)[] Sources =
    [
        ("walk_in", "Pasó por el gimnasio"),
        ("referral", "Referido"),
        ("social_media", "Redes sociales"),
        ("google", "Google"),
        ("flyer", "Volante"),
        ("corporate", "Convenio empresarial"),
        ("other", "Otro"),
    ];

    public static readonly (string Value, string Label)[] Goals =
    [
        ("weight_loss", "Bajar de peso"),
        ("muscle_gain", "Ganar músculo"),
        ("fitness", "Estar en forma"),
        ("health", "Salud"),
        ("sport", "Deporte"),
        ("other", "Otro"),
    ];

    [GeneratedRegex(@"^\+[1-9][0-9]{7,14}$")]
    private static partial Regex E164();

    public static string DocumentType(string? value) => Find(DocumentTypes, value);

    /// <summary>Sigla corta para listas: <c>CC</c>, <c>TI</c>, <c>PPT</c>…</summary>
    public static string DocumentShort(string? value) => value switch
    {
        "other" => "Doc.",
        null or "" => string.Empty,
        _ => value.ToUpperInvariant(),
    };

    public static string Gender(string? value) => Find(Genders, value);

    public static string Source(string? value) => Find(Sources, value);

    public static string Goal(string? value) => Find(Goals, value);

    public static string FieldLabel(string field) => field switch
    {
        "created" => "Ficha creada",
        "status" => "Estado",
        "first_name" => "Nombres",
        "last_name" => "Apellidos",
        "document_type" => "Tipo de documento",
        "document_number" => "Número de documento",
        "birth_date" => "Fecha de nacimiento",
        "gender" => "Género",
        "photo_url" => "Foto",
        "phone" => "Teléfono",
        "email" => "Email",
        "address" => "Dirección",
        "city" => "Ciudad",
        "emergency_contact_name" => "Contacto de emergencia: nombre",
        "emergency_contact_relationship" => "Contacto de emergencia: parentesco",
        "emergency_contact_phone" => "Contacto de emergencia: teléfono",
        "source" => "Cómo nos conoció",
        "referred_by_member_id" => "Referido por",
        "goal" => "Objetivo",
        "trainer" => "Entrenador",
        "tags" => "Etiquetas",
        "guardian_name" => "Acudiente: nombre",
        "guardian_document_type" => "Acudiente: tipo de documento",
        "guardian_document_number" => "Acudiente: documento",
        "guardian_phone" => "Acudiente: teléfono",
        "guardian_relationship" => "Acudiente: parentesco",
        "joined_at" => "Fecha de ingreso",
        "consent_data_processing" => "Autorización de datos",
        "consent_liability_waiver" => "Exoneración de responsabilidad",
        _ => field,
    };

    /// <summary>El valor de un cambio del historial, traducido cuando el campo es un enum.</summary>
    public static string ChangeValue(string field, string? value)
    {
        if (string.IsNullOrEmpty(value))
        {
            return "—";
        }

        return field switch
        {
            "document_type" or "guardian_document_type" => DocumentType(value),
            "gender" => Gender(value),
            "source" => Source(value),
            "goal" => Goal(value),
            "status" => value == "active" ? "Activo" : value == "inactive" ? "Inactivo" : value,
            "consent_data_processing" or "consent_liability_waiver" => ConsentLabels.ChangeValue(value),
            "photo_url" => "(imagen)",
            "referred_by_member_id" => "(otro miembro)",
            _ => value,
        };
    }

    public static string ChangeRole(string? role) => role switch
    {
        "owner" => "Dueño",
        "staff" => "Staff",
        "member" => "Miembro",
        "superadmin" => "SuperAdmin",
        "system" => "Sistema",
        _ => role ?? string.Empty,
    };

    public static string ChangeSource(string? source) => source switch
    {
        "panel" => "Panel",
        "app" => "App",
        "import" => "Importación",
        "system" => "Sistema",
        _ => source ?? string.Empty,
    };

    public static bool IsMinor(DateOnly? birthDate, DateOnly today)
        => birthDate is { } birth && birth.AddYears(AdultAge) > today;

    public static bool IsE164(string? phone) => phone is not null && E164().IsMatch(phone);

    /// <summary>
    /// Por qué una ficha está incompleta (vacío si no lo está): sin fecha de nacimiento, o con un
    /// teléfono que no tiene formato internacional.
    /// </summary>
    public static IReadOnlyList<string> IncompleteReasons(DateOnly? birthDate, string? phone)
    {
        var reasons = new List<string>();
        if (birthDate is null)
        {
            reasons.Add("falta la fecha de nacimiento");
        }

        if (!string.IsNullOrWhiteSpace(phone) && !IsE164(phone))
        {
            reasons.Add("el teléfono no tiene formato internacional (+57…)");
        }

        return reasons;
    }

    private static string Find((string Value, string Label)[] options, string? value)
    {
        if (string.IsNullOrEmpty(value))
        {
            return string.Empty;
        }

        foreach (var (v, label) in options)
        {
            if (v == value)
            {
                return label;
            }
        }

        return value;
    }
}
