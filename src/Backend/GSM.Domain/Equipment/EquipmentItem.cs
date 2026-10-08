using GSM.Domain.Common;
using GSM.Domain.Common.Exceptions;

namespace GSM.Domain.Equipment;

/// <summary>
/// Una máquina o equipo del inventario de un gym. Las máquinas se registran una fila por unidad
/// (importa saber cuál está dañada); los implementos sueltos van en una fila con <see cref="Quantity"/>.
/// </summary>
public sealed class EquipmentItem : BaseEntity, ITenantEntity
{
    public const int ZoneMaxLength = 60;
    public const int ShortTextMaxLength = 120;
    public const int MaxQuantity = 1000;

    private EquipmentItem()
    {
    }

    public Guid GymId { get; private set; }

    /// <summary>Modelo del catálogo del que salió. <c>null</c> = máquina propia del gym, creada a mano.</summary>
    public Guid? ModelId { get; private set; }

    public string Name { get; private set; } = string.Empty;

    public EquipmentType Type { get; private set; }

    /// <summary>Foto de la máquina real. Si falta, se muestra la imagen del modelo y, si tampoco hay, el ícono del tipo.</summary>
    public string? PhotoUrl { get; private set; }

    public string? Zone { get; private set; }

    public string? Brand { get; private set; }

    public string? Model { get; private set; }

    /// <summary>Ajustes de esta máquina; lo ve el miembro. Se precarga del modelo.</summary>
    public string? Instructions { get; private set; }

    public int Quantity { get; private set; } = 1;

    public EquipmentStatus Status { get; private set; } = EquipmentStatus.Available;

    public DateOnly? ExpectedBackOn { get; private set; }

    public DateOnly? AcquiredOn { get; private set; }

    /// <summary>Internas, solo staff.</summary>
    public string? Notes { get; private set; }

    /// <summary>Baja lógica (se vendió o se botó). Distinto de "fuera de servicio".</summary>
    public bool IsActive { get; private set; } = true;

    public static EquipmentItem Create(
        Guid gymId,
        Guid? modelId,
        string name,
        EquipmentType type,
        string? zone,
        string? brand,
        string? model,
        string? instructions,
        int quantity,
        DateOnly? acquiredOn,
        string? notes)
    {
        if (gymId == Guid.Empty)
        {
            throw new ValidationDomainException("El equipo necesita un gym.");
        }

        var item = new EquipmentItem
        {
            GymId = gymId,
            ModelId = modelId == Guid.Empty ? null : modelId,
        };

        item.Apply(name, type, zone, brand, model, instructions, quantity, acquiredOn, notes);
        return item;
    }

    public void Update(
        string name,
        EquipmentType type,
        string? zone,
        string? brand,
        string? model,
        string? instructions,
        int quantity,
        DateOnly? acquiredOn,
        string? notes)
        => Apply(name, type, zone, brand, model, instructions, quantity, acquiredOn, notes);

    public void SetPhoto(string? photoUrl) => PhotoUrl = EquipmentText.Optional(photoUrl, 2048);

    /// <summary>Baja lógica. Idempotente.</summary>
    public void Deactivate() => IsActive = false;

    private void Apply(
        string name,
        EquipmentType type,
        string? zone,
        string? brand,
        string? model,
        string? instructions,
        int quantity,
        DateOnly? acquiredOn,
        string? notes)
    {
        if (!Enum.IsDefined(type))
        {
            throw new ValidationDomainException("Tipo de equipo no válido.");
        }

        if (quantity is < 1 or > MaxQuantity)
        {
            throw new ValidationDomainException($"La cantidad debe estar entre 1 y {MaxQuantity}.");
        }

        Name = EquipmentText.Name(name, "equipo");
        Type = type;
        Zone = EquipmentText.Optional(zone, ZoneMaxLength);
        Brand = EquipmentText.Optional(brand, ShortTextMaxLength);
        Model = EquipmentText.Optional(model, ShortTextMaxLength);
        Instructions = EquipmentText.Optional(instructions, EquipmentModel.TextMaxLength);
        Quantity = quantity;
        AcquiredOn = acquiredOn;
        Notes = EquipmentText.Optional(notes, EquipmentModel.TextMaxLength);
    }
}
