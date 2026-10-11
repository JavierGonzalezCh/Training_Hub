using GSM.Domain.Equipment;

namespace GSM.Application.Equipment.Abstractions;

/// <summary>Filtro del inventario del gym. <c>Zone</c> compara sin distinguir mayúsculas.</summary>
public sealed record EquipmentFilter(
    EquipmentType? Type = null,
    string? Zone = null,
    EquipmentStatus? Status = null,
    string? Search = null,
    bool IncludeInactive = false);

/// <summary>Catálogo del sistema (<see cref="EquipmentModel"/>): global, solo lectura en E1.</summary>
public interface IEquipmentModelRepository
{
    Task<IReadOnlyList<EquipmentModel>> ListActiveAsync(EquipmentType? type, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<EquipmentModel>> GetByIdsAsync(
        IReadOnlyCollection<Guid> ids, CancellationToken cancellationToken = default);
}

/// <summary>
/// Inventario del gym. El Global Query Filter ya restringe al gym actual. Sin <c>SaveChanges</c>.
/// </summary>
public interface IEquipmentItemRepository
{
    Task<EquipmentItem?> GetAsync(Guid id, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<EquipmentItem>> ListAsync(EquipmentFilter filter, CancellationToken cancellationToken = default);

    /// <summary>Zonas distintas de las máquinas activas, para autocompletar.</summary>
    Task<IReadOnlyList<string>> ListZonesAsync(CancellationToken cancellationToken = default);

    /// <summary>Cuántas filas activas hay ya del modelo (para seguir la numeración "Bicicleta 4").</summary>
    Task<int> CountActiveByModelAsync(Guid modelId, CancellationToken cancellationToken = default);

    void Add(EquipmentItem item);
}
