using GSM.Domain.Common;
using GSM.Domain.Common.Exceptions;

namespace GSM.Domain.Equipment;

/// <summary>
/// Máquina o equipo común del catálogo del sistema. Global (sin <c>gym_id</c>): lo siembra una
/// migración de datos y solo lo edita el SuperAdmin (S4). No implementa <see cref="ITenantEntity"/>.
/// </summary>
public sealed class EquipmentModel : BaseEntity
{
    public const int NameMaxLength = 120;
    public const int TextMaxLength = 1000;

    private EquipmentModel()
    {
    }

    public string Name { get; private set; } = string.Empty;

    public EquipmentType Type { get; private set; }

    /// <summary>Imagen de referencia. Es trabajo de contenido: mientras falte, la UI muestra un ícono por tipo.</summary>
    public string? ImageUrl { get; private set; }

    public string? Description { get; private set; }

    /// <summary>Ajustes típicos; el gym puede reemplazarlos por los de su máquina.</summary>
    public string? DefaultInstructions { get; private set; }

    /// <summary><c>true</c> en máquinas (una fila por unidad); <c>false</c> en implementos sueltos (con cantidad).</summary>
    public bool TrackedPerUnit { get; private set; }

    public bool IsActive { get; private set; } = true;

    public static EquipmentModel Create(
        string name,
        EquipmentType type,
        string? description,
        string? defaultInstructions,
        bool trackedPerUnit,
        string? imageUrl = null)
    {
        if (!Enum.IsDefined(type))
        {
            throw new ValidationDomainException("Tipo de equipo no válido.");
        }

        return new EquipmentModel
        {
            Name = EquipmentText.Name(name, "modelo"),
            Type = type,
            Description = EquipmentText.Optional(description, TextMaxLength),
            DefaultInstructions = EquipmentText.Optional(defaultInstructions, TextMaxLength),
            TrackedPerUnit = trackedPerUnit,
            ImageUrl = EquipmentText.Optional(imageUrl, 2048),
        };
    }
}

internal static class EquipmentText
{
    public static string Name(string? value, string what)
    {
        var name = (value ?? string.Empty).Trim();
        if (name.Length is < 2 or > EquipmentModel.NameMaxLength)
        {
            throw new ValidationDomainException(
                $"El nombre del {what} debe tener entre 2 y {EquipmentModel.NameMaxLength} caracteres.");
        }

        return name;
    }

    public static string? Optional(string? value, int maxLength)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        var trimmed = value.Trim();
        if (trimmed.Length > maxLength)
        {
            throw new ValidationDomainException($"El texto no puede exceder {maxLength} caracteres.");
        }

        return trimmed;
    }
}
