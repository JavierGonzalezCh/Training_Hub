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

    /// <summary>
    /// Las máquinas activas del gym (de cualquier estado), con lo que Routines necesita para ligar ejercicios,
    /// avisar en el editor y mostrar foto, zona e instrucciones. Vacío = el gym no tiene inventario.
    /// </summary>
    Task<IReadOnlyList<EquipmentItemInfo>> GetActiveItemsAsync(Guid gymId, CancellationToken cancellationToken = default);

    /// <summary>Modelos del catálogo del sistema que existen entre los <paramref name="modelIds"/> (para validar ligas).</summary>
    Task<IReadOnlyList<EquipmentModelInfo>> GetModelsAsync(
        IReadOnlyCollection<Guid> modelIds, CancellationToken cancellationToken = default);
}

/// <summary>
/// Una máquina del gym vista desde otro módulo. <c>PhotoUrl</c> ya resuelve la cadena foto propia → imagen del
/// modelo → <c>null</c> (la UI pone entonces el ícono del tipo).
/// </summary>
public sealed record EquipmentItemInfo(
    Guid Id,
    Guid? ModelId,
    string Name,
    EquipmentType Type,
    string? PhotoUrl,
    string? Zone,
    string? Instructions,
    EquipmentStatus Status,
    DateOnly? ExpectedBackOn);

public sealed record EquipmentModelInfo(Guid Id, string Name, EquipmentType Type);

public sealed record GymEquipmentAvailability(bool HasInventory, IReadOnlySet<EquipmentType> AvailableTypes)
{
    /// <summary>¿El gym puede hacer un ejercicio que pide estos tipos de equipo? Sin inventario, siempre sí.</summary>
    public bool CoversAll(IEnumerable<EquipmentType> required)
        => !HasInventory || required.All(t => t == EquipmentType.Bodyweight || AvailableTypes.Contains(t));
}
