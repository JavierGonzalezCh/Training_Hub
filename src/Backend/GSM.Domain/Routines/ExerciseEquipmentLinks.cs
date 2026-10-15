using GSM.Domain.Common;
using GSM.Domain.Common.Exceptions;

namespace GSM.Domain.Routines;

/// <summary>
/// Liga de sistema: "este ejercicio se hace en este modelo del catálogo de máquinas". La define el SuperAdmin y
/// vale para todos los gyms: cuando un gym tiene una máquina de ese modelo, el ejercicio queda ligado a ella.
/// No es de un gym (<c>gym_id</c> no existe). <see cref="ModelId"/> apunta a <c>equipment.equipment_models</c>
/// sin FK: no hay claves foráneas entre schemas de módulos distintos.
/// </summary>
public sealed class ExerciseEquipmentModel : BaseEntity
{
    private ExerciseEquipmentModel()
    {
    }

    public Guid ExerciseId { get; private set; }

    public Guid ModelId { get; private set; }

    public static ExerciseEquipmentModel Create(Guid exerciseId, Guid modelId)
    {
        if (exerciseId == Guid.Empty || modelId == Guid.Empty)
        {
            throw new ValidationDomainException("La liga necesita un ejercicio y un modelo.");
        }

        return new ExerciseEquipmentModel { ExerciseId = exerciseId, ModelId = modelId };
    }
}

/// <summary>
/// Liga del gym: "este ejercicio se hace en esta máquina mía". Sirve para sus máquinas propias o para corregir
/// el ligado automático por modelo: si un ejercicio tiene ligas del gym, solo esas cuentan en ese gym.
/// <see cref="EquipmentItemId"/> apunta a <c>equipment.equipment_items</c> sin FK.
/// </summary>
public sealed class ExerciseEquipmentItem : BaseEntity, ITenantEntity
{
    private ExerciseEquipmentItem()
    {
    }

    public Guid GymId { get; private set; }

    public Guid ExerciseId { get; private set; }

    public Guid EquipmentItemId { get; private set; }

    public static ExerciseEquipmentItem Create(Guid gymId, Guid exerciseId, Guid equipmentItemId)
    {
        if (gymId == Guid.Empty || exerciseId == Guid.Empty || equipmentItemId == Guid.Empty)
        {
            throw new ValidationDomainException("La liga necesita un gym, un ejercicio y una máquina.");
        }

        return new ExerciseEquipmentItem { GymId = gymId, ExerciseId = exerciseId, EquipmentItemId = equipmentItemId };
    }
}
