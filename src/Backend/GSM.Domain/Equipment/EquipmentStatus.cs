namespace GSM.Domain.Equipment;

/// <summary>
/// Estado de una máquina del inventario. Solo <see cref="Available"/> se puede usar. Todas nacen
/// disponibles; cambiarlo deja una fila de <see cref="EquipmentStatusChange"/> (E2).
/// </summary>
public enum EquipmentStatus
{
    Available,
    Maintenance,
    OutOfService,
}
