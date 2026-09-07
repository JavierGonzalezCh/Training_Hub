using System.Text.RegularExpressions;
using GSM.Domain.Common;
using GSM.Domain.Common.Exceptions;

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

    public string? Nit { get; private set; }

    public string? Phone { get; private set; }

    public string? Email { get; private set; }

    public string? LogoUrl { get; private set; }

    public string PrimaryColor { get; private set; } = DefaultPrimaryColor;

    public string SecondaryColor { get; private set; } = DefaultSecondaryColor;

    public bool IsActive { get; private set; } = true;

    public GymPlan Plan { get; private set; } = GymPlan.Basic;

    public GymStatus Status { get; private set; } = GymStatus.Trial;

    public Guid OwnerId { get; private set; }

    Guid ITenantEntity.GymId => Id;

    public const string DefaultPrimaryColor = "#2563EB";
    public const string DefaultSecondaryColor = "#1E293B";

    public static Gym Create(
        string name,
        string slug,
        GymPlan plan = GymPlan.Basic,
        string? nit = null,
        string? phone = null,
        string? email = null,
        string? primaryColor = null,
        string? secondaryColor = null)
        => new()
        {
            Name = NormalizeName(name),
            Slug = NormalizeSlug(slug),
            Nit = Trim(nit),
            Phone = Trim(phone),
            Email = Trim(email),
            PrimaryColor = NormalizeColor(primaryColor, DefaultPrimaryColor),
            SecondaryColor = NormalizeColor(secondaryColor, DefaultSecondaryColor),
            Plan = plan,
            Status = GymStatus.Trial,
            IsActive = true,
        };

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

    public void UpdateProfile(string name, string? nit, string? phone, string? email)
    {
        Name = NormalizeName(name);
        Nit = Trim(nit);
        Phone = Trim(phone);
        Email = Trim(email);
    }

    public void UpdateBranding(string? logoUrl, string primaryColor, string secondaryColor)
    {
        LogoUrl = Trim(logoUrl);
        PrimaryColor = NormalizeColor(primaryColor, DefaultPrimaryColor, allowFallback: false);
        SecondaryColor = NormalizeColor(secondaryColor, DefaultSecondaryColor, allowFallback: false);
    }

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
