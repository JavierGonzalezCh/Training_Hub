using GSM.Domain.Equipment;

namespace GSM.Application.Equipment.Public;

/// <summary>Lectura que el módulo Equipment expone a los demás (Routines la usa para el nivel 1).</summary>
public interface IEquipmentReader
{
    /// <summary>
    /// Qué tipos de equipo tiene hoy el gym disponibles. Un tipo cuenta si existe al menos una máquina
    /// activa y disponible de ese tipo; el peso corporal siempre cuenta. Un gym sin inventario cargado
    /// (ninguna máquina activa) devuelve <see cref="GymEquipmentAvailability.HasInventory"/> en
    /// <c>false</c>: quien consulte no debe filtrar nada.
    /// </summary>
    Task<GymEquipmentAvailability> GetAvailabilityAsync(Guid gymId, CancellationToken cancellationToken = default);
}

public sealed record GymEquipmentAvailability(bool HasInventory, IReadOnlySet<EquipmentType> AvailableTypes)
{
    /// <summary>¿El gym puede hacer un ejercicio que pide estos tipos de equipo? Sin inventario, siempre sí.</summary>
    public bool CoversAll(IEnumerable<EquipmentType> required)
        => !HasInventory || required.All(t => t == EquipmentType.Bodyweight || AvailableTypes.Contains(t));
}
