using GSM.Domain.Common;
using GSM.Domain.Common.Exceptions;

namespace GSM.Domain.Classes;

/// <summary>
/// Liga una clase a una máquina del inventario (Classes v2, C6): el cupo de cada sesión no puede pasar de las
/// unidades de las máquinas ligadas que están disponibles ese día. La máquina vive en Equipment; aquí solo se
/// guarda su id (sin FK entre schemas: se valida contra <c>IEquipmentReader</c>).
/// </summary>
public sealed class ClassEquipmentItem : BaseEntity, ITenantEntity
{
    private ClassEquipmentItem()
    {
    }

    public Guid GymId { get; private set; }

    public Guid ClassId { get; private set; }

    public Guid EquipmentItemId { get; private set; }

    public static ClassEquipmentItem Link(Guid gymId, Guid classId, Guid equipmentItemId)
    {
        if (gymId == Guid.Empty || classId == Guid.Empty || equipmentItemId == Guid.Empty)
        {
            throw new ValidationDomainException("Datos de la máquina ligada incompletos.");
        }

        return new ClassEquipmentItem { GymId = gymId, ClassId = classId, EquipmentItemId = equipmentItemId };
    }
}
