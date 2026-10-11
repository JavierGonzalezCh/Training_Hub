using GSM.Domain.Common;
using GSM.Domain.Common.Exceptions;

namespace GSM.Domain.Equipment;

/// <summary>
/// Una fila del historial de estado de una máquina: de qué estado a cuál, por qué, quién y cuándo. Solo se
/// añaden filas; nunca se editan ni se borran. Es el historial de mantenimiento de la máquina.
/// </summary>
public sealed class EquipmentStatusChange : BaseEntity, ITenantEntity
{
    public const int ReasonMaxLength = 500;

    private EquipmentStatusChange()
    {
    }

    public Guid GymId { get; private set; }

    public Guid ItemId { get; private set; }

    public EquipmentStatus FromStatus { get; private set; }

    public EquipmentStatus ToStatus { get; private set; }

    /// <summary>Obligatorio al pasar a mantenimiento o fuera de servicio; opcional al volver a disponible.</summary>
    public string? Reason { get; private set; }

    /// <summary>Fecha de regreso esperada que quedó fijada con este cambio (solo si el nuevo estado no es disponible).</summary>
    public DateOnly? ExpectedBackOn { get; private set; }

    public Guid? ChangedByUserId { get; private set; }

    /// <summary>Instante del cambio, UTC.</summary>
    public DateTimeOffset ChangedAt { get; private set; }

    internal static EquipmentStatusChange Record(
        EquipmentItem item,
        EquipmentStatus from,
        EquipmentStatus to,
        string? reason,
        DateOnly? expectedBackOn,
        Guid? changedByUserId,
        DateTimeOffset changedAt)
        => new()
        {
            GymId = item.GymId,
            ItemId = item.Id,
            FromStatus = from,
            ToStatus = to,
            Reason = reason,
            ExpectedBackOn = expectedBackOn,
            ChangedByUserId = changedByUserId,
            ChangedAt = changedAt,
        };
}
