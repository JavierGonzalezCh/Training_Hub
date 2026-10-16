using GSM.Application.Classes.Abstractions;
using GSM.Application.Classes.Contracts;
using GSM.Application.Equipment.Public;
using GSM.Domain.Classes;
using GSM.Domain.Common.Exceptions;
using GSM.Domain.Equipment;

namespace GSM.Application.Classes;

/// <summary>
/// El cupo de una ocurrencia visto con las máquinas ligadas (C6). <see cref="Nominal"/> es el cupo de la clase (o de la
/// sesión materializada, que lo copió); <see cref="AvailableMachines"/> cuenta las unidades de las máquinas ligadas que
/// sirven ese día; <see cref="Effective"/> es el menor de los dos. Sin máquinas ligadas, <see cref="Effective"/> = <see cref="Nominal"/>.
/// </summary>
public sealed record SessionCapacity(int? Nominal, int? Effective, bool IsLinked, int LinkedMachines, int AvailableMachines)
{
    /// <summary>Las máquinas, y no el cupo de la clase, son lo que limita hoy.</summary>
    public bool LimitedByMachines => IsLinked && Nominal is { } nominal && Effective < nominal;
}

/// <summary>Las máquinas de una clase: cuántas ligas tiene y cuáles siguen activas en el inventario.</summary>
public sealed record ClassMachines(int LinkCount, IReadOnlyList<EquipmentItemInfo> Active);

/// <summary>
/// C6: calcula el cupo efectivo de forma perezosa, en el momento de reservar, anotarse en la lista de espera, ofrecer un
/// cupo o mirar el horario/roster. No se copia a la sesión (la sesión solo copia el cupo de la clase): así una máquina que
/// pasa a mantenimiento, o que vuelve, se refleja sin scheduler. Una máquina sirve en la fecha D si está disponible o si su
/// fecha de regreso esperada es D o anterior (Equipment solo guarda el estado vigente, no una agenda).
/// Classes lee a Equipment únicamente por <see cref="IEquipmentReader"/>.
/// </summary>
public sealed class ClassCapacityService(IClassEquipmentRepository links, IEquipmentReader equipment)
{
    public async Task<SessionCapacity> ForAsync(
        Guid gymId, Guid classId, int? nominal, DateOnly date, CancellationToken cancellationToken)
    {
        var map = await LoadAsync(gymId, [classId], cancellationToken);
        return Compute(nominal, map.GetValueOrDefault(classId), date);
    }

    /// <summary>Máquinas por clase, solo de las clases que tienen alguna ligada.</summary>
    public async Task<IReadOnlyDictionary<Guid, ClassMachines>> LoadAsync(
        Guid gymId, IReadOnlyCollection<Guid> classIds, CancellationToken cancellationToken)
    {
        var rows = await links.ListForClassesAsync(classIds, cancellationToken);
        if (rows.Count == 0)
        {
            return new Dictionary<Guid, ClassMachines>();
        }

        var items = (await equipment.GetActiveItemsAsync(gymId, cancellationToken)).ToDictionary(i => i.Id);
        return rows
            .GroupBy(l => l.ClassId)
            .ToDictionary(
                g => g.Key,
                g => new ClassMachines(
                    g.Count(),
                    g.Select(l => items.GetValueOrDefault(l.EquipmentItemId)).OfType<EquipmentItemInfo>().ToList()));
    }

    public static SessionCapacity Compute(int? nominal, ClassMachines? machines, DateOnly date)
    {
        if (machines is null || machines.LinkCount == 0)
        {
            return new SessionCapacity(nominal, nominal, false, 0, 0);
        }

        var total = machines.Active.Sum(i => i.Quantity);
        var available = machines.Active.Where(i => AvailableOn(i, date)).Sum(i => i.Quantity);
        int? effective = nominal is { } n ? Math.Min(n, available) : null;
        return new SessionCapacity(nominal, effective, true, total, available);
    }

    public static bool AvailableOn(EquipmentItemInfo item, DateOnly date)
        => item.Status == EquipmentStatus.Available || (item.ExpectedBackOn is { } back && back <= date);

    /// <summary>Anota en cada entrada de una clase con máquinas su cupo efectivo y las máquinas disponibles ese día.</summary>
    public async Task<IReadOnlyList<ScheduleEntry>> EnrichAsync(
        Guid gymId, IReadOnlyList<ScheduleEntry> entries, CancellationToken cancellationToken)
    {
        var map = await LoadAsync(gymId, entries.Select(e => e.ClassId).Distinct().ToList(), cancellationToken);
        if (map.Count == 0)
        {
            return entries;
        }

        return entries
            .Select(e =>
            {
                if (!map.TryGetValue(e.ClassId, out var machines))
                {
                    return e;
                }

                var cap = Compute(e.Capacity, machines, e.SessionDate);
                return e with
                {
                    EffectiveCapacity = cap.Effective,
                    AvailableMachines = cap.AvailableMachines,
                    LinkedMachines = cap.LinkedMachines,
                };
            })
            .ToList();
    }
}

/// <summary>Validación y sincronización de las máquinas ligadas al guardar una clase.</summary>
internal static class ClassEquipmentLinks
{
    /// <summary>
    /// 422 si alguna máquina no existe en el inventario activo del gym, o si se liga a una clase de entrada libre
    /// (sin cupo no hay nada que limitar). Devuelve los ids sin repetir.
    /// </summary>
    public static async Task<IReadOnlyList<Guid>> ValidateAsync(
        IEquipmentReader equipment, Guid gymId, IReadOnlyList<Guid>? requested, int? capacity, CancellationToken cancellationToken)
    {
        var ids = (requested ?? []).Distinct().ToList();
        if (ids.Count == 0)
        {
            return ids;
        }

        if (capacity is null)
        {
            throw new ValidationDomainException(
                "Una clase de entrada libre no puede ligarse a máquinas: indica un cupo para limitarlo por las máquinas.");
        }

        var active = (await equipment.GetActiveItemsAsync(gymId, cancellationToken)).Select(i => i.Id).ToHashSet();
        if (ids.Any(id => !active.Contains(id)))
        {
            throw new ValidationDomainException("Alguna de las máquinas elegidas no existe en el inventario o está dada de baja.");
        }

        return ids;
    }

    /// <summary>Deja ligadas exactamente <paramref name="desired"/>: agrega las nuevas y quita las que sobran.</summary>
    public static void Sync(
        IClassEquipmentRepository repository, Guid gymId, Guid classId,
        IReadOnlyList<ClassEquipmentItem> existing, IReadOnlyList<Guid> desired)
    {
        foreach (var link in existing.Where(l => !desired.Contains(l.EquipmentItemId)))
        {
            repository.Remove(link);
        }

        var have = existing.Select(l => l.EquipmentItemId).ToHashSet();
        foreach (var id in desired.Where(id => !have.Contains(id)))
        {
            repository.Add(ClassEquipmentItem.Link(gymId, classId, id));
        }
    }
}
