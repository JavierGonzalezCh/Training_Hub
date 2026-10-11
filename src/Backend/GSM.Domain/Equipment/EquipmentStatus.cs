namespace GSM.Domain.Equipment;

/// <summary>
/// Estado de una máquina del inventario. Solo <see cref="Available"/> se puede usar. En E1 todas
/// nacen disponibles; cambiar el estado (con motivo e historial) es la sub-etapa E2.
/// </summary>
public enum EquipmentStatus
{
    Available,
    Maintenance,
    OutOfService,
}
