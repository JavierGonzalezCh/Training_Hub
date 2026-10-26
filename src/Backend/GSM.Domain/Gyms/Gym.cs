using System.Text.RegularExpressions;
using GSM.Domain.Common;
using GSM.Domain.Common.Exceptions;
using GSM.Domain.Members;

namespace GSM.Domain.Gyms;

/// <summary>
/// Tenant raíz. Cada tabla de negocio referencia su <c>gym_id</c>. El branding vive inline
/// (sin entidad de tema aparte en el MVP). <see cref="OwnerId"/> es el usuario owner fundador;
/// no lleva FK (vive en otro schema) y se valida en el caso de uso.
/// </summary>
public sealed partial class Gym : BaseEntity, ITenantEntity
{
    private Gym()
    {
    }

    public string Name { get; private set; } = string.Empty;

    public string Slug { get; private set; } = string.Empty;

    /// <summary>Razón social, para recibos.</summary>
    public string? LegalName { get; private set; }

    public string? Nit { get; private set; }

    /// <summary>Teléfono en E.164 (<c>+573001234567</c>).</summary>
    public string? Phone { get; private set; }

    public string? Email { get; private set; }

    public string? LogoUrl { get; private set; }

    public string PrimaryColor { get; private set; } = DefaultPrimaryColor;

    public string SecondaryColor { get; private set; } = DefaultSecondaryColor;

    /// <summary>
    /// Aforo aproximado: cuántos clientes caben a la vez en el gimnasio, según el dueño. <c>null</c> si
    /// aún no lo ha dicho. Base para la tasa de ocupación en tiempo real.
    /// </summary>
    public int? Capacity { get; private set; }

    public string? Address { get; private set; }

    public string? City { get; private set; }

    public double? Latitude { get; private set; }

    public double? Longitude { get; private set; }

    /// <summary>Radio de la geocerca en metros. Lo usará Attendance (A6).</summary>
    public int GeofenceRadiusMeters { get; private set; } = DefaultGeofenceRadiusMeters;

    public string? InstagramUrl { get; private set; }

    public string? FacebookUrl { get; private set; }

    public string? TikTokUrl { get; private set; }

    public string? WebsiteUrl { get; private set; }

    /// <summary>WhatsApp del gym en E.164, para abrir el chat con <c>wa.me</c>.</summary>
    public string? WhatsAppPhone { get; private set; }

    public const int DefaultGeofenceRadiusMeters = 150;

    public const int MinGeofenceRadiusMeters = 20;

    public const int MaxGeofenceRadiusMeters = 2000;

    public bool IsActive { get; private set; } = true;

    public GymStatus Status { get; private set; } = GymStatus.Trial;

    /// <summary>Tipo de negocio: etiqueta y plantilla de arranque. Los gyms anteriores son <see cref="GymType.Gym"/>.</summary>
    public GymType Type { get; private set; } = GymType.Gym;

    /// <summary>
    /// El Owner fundador (histórico). Los Owners de hoy son los accesos <c>owner</c> de Identity (Gyms §6); este
    /// campo ya no manda sobre nada.
    /// </summary>
    public Guid OwnerId { get; private set; }

    /// <summary>
    /// Cuenta del Owner marcada como contacto de facturación (cobros del SaaS, avisos de suscripción). Sin FK:
    /// vive en otro schema; el caso de uso valida que sea un Owner activo. <c>null</c> si nunca se eligió: se
    /// lee entonces como el primer Owner activo.
    /// </summary>
    public Guid? BillingContactUserId { get; private set; }

    Guid ITenantEntity.GymId => Id;

    /// <summary>
    /// Colores de marca por defecto: los de la identidad visual v4.0 (rojo GSM / cian).
    /// Un gym que no define los suyos hereda la marca del producto, no el azul de Material.
    /// </summary>
    public const string DefaultPrimaryColor = "#B50016";

    public const string DefaultSecondaryColor = "#0AAFCA";

    public static Gym Create(
        string name,
        string slug,
        string? nit = null,
        string? phone = null,
        string? email = null,
        string? primaryColor = null,
        string? secondaryColor = null,
        GymType type = GymType.Gym)
    {
        if (!Enum.IsDefined(type))
        {
            throw new ValidationDomainException("El tipo de negocio no es válido.");
        }

        return new()
        {
            Type = type,
            Name = NormalizeName(name),
            Slug = NormalizeSlug(slug),
            Nit = Trim(nit),
            Phone = NormalizePhone(phone, "El teléfono"),
            Email = Trim(email),
            PrimaryColor = NormalizeColor(primaryColor, DefaultPrimaryColor),
            SecondaryColor = NormalizeColor(secondaryColor, DefaultSecondaryColor),
            Status = GymStatus.Trial,
            IsActive = true,
        };
    }

    /// <summary>Cambia el tipo de negocio. No reaplica ninguna plantilla (docs/GSM-tipos-de-negocio.md §9).</summary>
    public void ChangeType(GymType type)
    {
        if (!Enum.IsDefined(type))
        {
            throw new ValidationDomainException("El tipo de negocio no es válido.");
        }

        Type = type;
    }

    /// <summary>Fija el owner fundador. Solo una vez, durante el alta del gym.</summary>
    public void AssignOwner(Guid ownerId)
    {
        if (ownerId == Guid.Empty)
        {
            throw new ValidationDomainException("El gym necesita un owner.");
        }

        if (OwnerId != Guid.Empty)
        {
            throw new ConflictException("El gym ya tiene un owner asignado.");
        }

        OwnerId = ownerId;
    }

    /// <summary>Marca al contacto de facturación (un Owner; lo valida el caso de uso) o lo deja sin elegir.</summary>
    public void SetBillingContact(Guid? userId)
    {
        if (userId == Guid.Empty)
        {
            throw new ValidationDomainException("El contacto de facturación no es válido.");
        }

        BillingContactUserId = userId;
    }

    public void UpdateProfile(string name, string? nit, string? phone, string? email, string? legalName = null)
    {
        Name = NormalizeName(name);
        LegalName = Trim(legalName);
        Nit = Trim(nit);
        Phone = NormalizePhone(phone, "El teléfono");
        Email = Trim(email);
    }

    /// <summary>
    /// Ubicación del gym. Dirección y ciudad son opcionales en el dominio (los gyms anteriores no las
    /// tienen); el formulario las exige. Las coordenadas van juntas o ninguna.
    /// </summary>
    public void UpdateLocation(
        string? address, string? city, double? latitude, double? longitude, int? geofenceRadiusMeters = null)
    {
        if (latitude is null != longitude is null)
        {
            throw new ValidationDomainException("La latitud y la longitud van juntas: indica las dos o ninguna.");
        }

        if (latitude is < -90 or > 90 || latitude is double.NaN)
        {
            throw new ValidationDomainException("La latitud debe estar entre -90 y 90.");
        }

        if (longitude is < -180 or > 180 || longitude is double.NaN)
        {
            throw new ValidationDomainException("La longitud debe estar entre -180 y 180.");
        }

        var radius = geofenceRadiusMeters ?? GeofenceRadiusMeters;
        if (radius is < MinGeofenceRadiusMeters or > MaxGeofenceRadiusMeters)
        {
            throw new ValidationDomainException(
                $"El radio de la geocerca debe estar entre {MinGeofenceRadiusMeters} y {MaxGeofenceRadiusMeters} metros.");
        }

        Address = Trim(address);
        City = Trim(city);
        Latitude = latitude;
        Longitude = longitude;
        GeofenceRadiusMeters = radius;
    }

    /// <summary>Redes y contacto de marca que ve el miembro. Las URL deben ser http(s) absolutas.</summary>
    public void UpdateSocial(
        string? instagramUrl, string? facebookUrl, string? tikTokUrl, string? websiteUrl, string? whatsAppPhone)
    {
        InstagramUrl = NormalizeUrl(instagramUrl, "La URL de Instagram");
        FacebookUrl = NormalizeUrl(facebookUrl, "La URL de Facebook");
        TikTokUrl = NormalizeUrl(tikTokUrl, "La URL de TikTok");
        WebsiteUrl = NormalizeUrl(websiteUrl, "La URL del sitio web");
        WhatsAppPhone = NormalizePhone(whatsAppPhone, "El WhatsApp");
    }

    /// <summary>Una URL absoluta http(s) de hasta 2048 caracteres, o <c>null</c> si viene vacía.</summary>
    public static bool IsValidUrl(string? value)
        => Uri.TryCreate(value?.Trim(), UriKind.Absolute, out var uri)
           && uri.Scheme is "http" or "https"
           && value!.Trim().Length <= 2048;

    private static string? NormalizeUrl(string? value, string label)
    {
        var trimmed = Trim(value);
        if (trimmed is null)
        {
            return null;
        }

        if (!IsValidUrl(trimmed))
        {
            throw new ValidationDomainException($"{label} no es válida: debe empezar por http:// o https://.");
        }

        return trimmed;
    }

    private static string? NormalizePhone(string? value, string label)
    {
        var trimmed = Trim(value);
        if (trimmed is null)
        {
            return null;
        }

        return PhoneNumber.TryNormalize(trimmed)
            ?? throw new ValidationDomainException(
                $"{label} no es un número válido. Escríbelo con indicativo, por ejemplo +573001234567.");
    }

    public const int MaxCapacity = 5000;

    public void UpdateCapacity(int? capacity)
    {
        if (capacity is < 1 or > MaxCapacity)
        {
            throw new ValidationDomainException($"El aforo debe estar entre 1 y {MaxCapacity} clientes.");
        }

        Capacity = capacity;
    }

    public void UpdateBranding(string? logoUrl, string primaryColor, string secondaryColor)
    {
        LogoUrl = Trim(logoUrl);
        PrimaryColor = NormalizeColor(primaryColor, DefaultPrimaryColor, allowFallback: false);
        SecondaryColor = NormalizeColor(secondaryColor, DefaultSecondaryColor, allowFallback: false);
    }

    /// <summary>Fija solo el logo (subida de archivo), sin tocar los colores.</summary>
    public void SetLogo(string? logoUrl) => LogoUrl = Trim(logoUrl);

    /// <summary>
    /// Fija el estado comercial manual (<c>trial</c>/<c>active</c>/<c>suspended</c>). Lo escribe
    /// SuperAdmin al suspender o reactivar un gym, vía <c>Gyms.Public.IGymAdministration</c>.
    /// Los estados <c>overdue</c> y <c>disabled</c> no viven aquí: se derivan de la suscripción.
    /// </summary>
    public void SetCommercialStatus(GymStatus status) => Status = status;

    public void Activate() => IsActive = true;

    public void Deactivate() => IsActive = false;

    private static string NormalizeName(string name)
    {
        var value = (name ?? string.Empty).Trim();
        if (value.Length is < 2 or > 100)
        {
            throw new ValidationDomainException("El nombre del gym debe tener entre 2 y 100 caracteres.");
        }

        return value;
    }

    private static string NormalizeSlug(string slug)
    {
        var value = (slug ?? string.Empty).Trim().ToLowerInvariant();
        if (value.Length is < 2 or > 60 || !SlugPattern().IsMatch(value))
        {
            throw new ValidationDomainException(
                "El slug debe ser minúsculas, dígitos y guiones (2-60 caracteres), sin guiones al inicio o final.");
        }

        return value;
    }

    private static string NormalizeColor(string? color, string fallback, bool allowFallback = true)
    {
        if (string.IsNullOrWhiteSpace(color))
        {
            if (allowFallback)
            {
                return fallback;
            }

            throw new ValidationDomainException("El color es obligatorio.");
        }

        var value = color.Trim().ToUpperInvariant();
        if (!HexColorPattern().IsMatch(value))
        {
            throw new ValidationDomainException($"'{color}' no es un color hex válido (#RRGGBB).");
        }

        return value;
    }

    private static string? Trim(string? value)
        => string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    [GeneratedRegex("^[a-z0-9]+(-[a-z0-9]+)*$")]
    private static partial Regex SlugPattern();

    [GeneratedRegex("^#[0-9A-F]{6}$")]
    private static partial Regex HexColorPattern();
}
