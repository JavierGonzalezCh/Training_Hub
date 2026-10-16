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

    /// <summary>Añade una fila al historial de estado (solo se añaden).</summary>
    void AddStatusChange(EquipmentStatusChange change);

    /// <summary>Historial de estado de una máquina, el más reciente primero.</summary>
    Task<IReadOnlyList<EquipmentStatusChange>> ListStatusChangesAsync(
        Guid itemId, CancellationToken cancellationToken = default);

    /// <summary>Las máquinas pedidas (de cualquier estado, incluso dadas de baja), sin seguimiento.</summary>
    Task<IReadOnlyDictionary<Guid, EquipmentItem>> GetByIdsAsync(
        IReadOnlyCollection<Guid> ids, CancellationToken cancellationToken = default);

    /// <summary>E4: cuántas máquinas activas hay en cada estado (para la tarjeta del dashboard).</summary>
    Task<IReadOnlyDictionary<EquipmentStatus, int>> CountActiveByStatusAsync(CancellationToken cancellationToken = default);
}

/// <summary>Filtro de los reportes de falla. Sin <c>Status</c> salen todos; <c>OpenOnly</c> deja los sin cerrar.</summary>
public sealed record EquipmentIssueFilter(
    EquipmentIssueStatus? Status = null,
    Guid? ItemId = null,
    bool OpenOnly = false);

/// <summary>E4: reportes de falla del gym actual. El Global Query Filter restringe al gym. Sin <c>SaveChanges</c>.</summary>
public interface IEquipmentIssueRepository
{
    void Add(EquipmentIssue issue);

    Task<EquipmentIssue?> GetAsync(Guid id, CancellationToken cancellationToken = default);

    /// <summary>Urgentes abiertos primero, luego los abiertos, y dentro de cada grupo los más recientes.</summary>
    Task<IReadOnlyList<EquipmentIssue>> ListAsync(
        EquipmentIssueFilter filter, int take, CancellationToken cancellationToken = default);

    /// <summary>Todos los reportes de una máquina (con seguimiento: el caso de uso puede marcarlos vistos).</summary>
    Task<IReadOnlyList<EquipmentIssue>> ListByItemAsync(Guid itemId, CancellationToken cancellationToken = default);

    /// <summary>Reporte sin cerrar de ese miembro sobre esa máquina, si lo hay.</summary>
    Task<EquipmentIssue?> FindUnclosedByReporterAsync(Guid itemId, Guid userId, CancellationToken cancellationToken = default);

    /// <summary>Los reportes de una cuenta, el más reciente primero.</summary>
    Task<IReadOnlyList<EquipmentIssue>> ListByReporterAsync(Guid userId, int take, CancellationToken cancellationToken = default);

    /// <summary>E4: reportes sin cerrar y, de ellos, urgentes (para la tarjeta del dashboard).</summary>
    Task<(int Open, int Urgent)> CountUnclosedAsync(CancellationToken cancellationToken = default);
}
