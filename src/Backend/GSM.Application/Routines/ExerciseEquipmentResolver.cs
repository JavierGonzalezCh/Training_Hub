using GSM.Application.Common.Abstractions;
using GSM.Application.Equipment.Public;
using GSM.Application.Routines.Abstractions;
using GSM.Application.Routines.Contracts;
using GSM.Domain.Equipment;
using GSM.Domain.Routines;

namespace GSM.Application.Routines;

/// <summary>Un ejercicio y los tipos de equipo que pide (<c>Exercise.Equipment</c>).</summary>
public sealed record ExerciseNeed(Guid ExerciseId, IReadOnlyList<EquipmentType> Types)
{
    public static ExerciseNeed Of(Exercise exercise) => new(exercise.Id, exercise.Equipment);
}

/// <summary>
/// "¿Se puede hacer este ejercicio hoy en este gym?" (<c>GSM-modulo-equipment.md</c> §5). Routines no lee la
/// entidad de Equipment: usa <see cref="IEquipmentReader"/>.
/// <list type="number">
///   <item>Si el ejercicio está ligado a máquinas del gym (liga propia del gym o, si no hay, por modelo del
///   sistema): sí, si al menos una está disponible. Las ligas del gym <b>reemplazan</b> a las de modelo.</item>
///   <item>Si no hay máquinas ligadas que el gym tenga: sí, si el gym tiene disponibles todos los tipos que pide.</item>
///   <item>Si el gym no tiene inventario (o la petición no es de un gym): sí.</item>
/// </list>
/// El resultado no trae entrada para los ejercicios sin información: quien consulta debe tratar la ausencia como
/// "disponible, sin máquinas".
/// </summary>
public sealed class ExerciseEquipmentResolver(
    ICurrentUserService currentUser,
    IExerciseEquipmentRepository links,
    IEquipmentReader equipment)
{
    public async Task<IReadOnlyDictionary<Guid, ExerciseEquipmentInfo>> ResolveAsync(
        IReadOnlyCollection<ExerciseNeed> needs, CancellationToken cancellationToken)
    {
        var result = new Dictionary<Guid, ExerciseEquipmentInfo>();
        if (needs.Count == 0 || currentUser.GymId is not { } gymId)
        {
            return result;
        }

        var inventory = await equipment.GetActiveItemsAsync(gymId, cancellationToken);
        if (inventory.Count == 0)
        {
            return result;
        }

        var ids = needs.Select(n => n.ExerciseId).Distinct().ToList();
        var modelLinks = (await links.ListModelLinksAsync(ids, cancellationToken))
            .ToLookup(l => l.ExerciseId, l => l.ModelId);
        var itemLinks = (await links.ListItemLinksAsync(ids, cancellationToken))
            .ToLookup(l => l.ExerciseId, l => l.EquipmentItemId);

        var availableTypes = inventory
            .Where(i => i.Status == EquipmentStatus.Available)
            .Select(i => i.Type)
            .ToHashSet();
        availableTypes.Add(EquipmentType.Bodyweight);
        var availability = new GymEquipmentAvailability(true, availableTypes);

        foreach (var need in needs)
        {
            var machines = CandidateMachines(inventory, itemLinks[need.ExerciseId], modelLinks[need.ExerciseId]);
            var available = machines.Count > 0
                ? machines.Any(m => m.Status == EquipmentStatus.Available)
                : availability.CoversAll(need.Types);

            result[need.ExerciseId] = new ExerciseEquipmentInfo(
                available,
                machines
                    .OrderBy(m => m.Status == EquipmentStatus.Available ? 0 : 1)
                    .ThenBy(m => m.Name)
                    .Select(ToMachine)
                    .ToList());
        }

        return result;
    }

    public async Task<ExerciseEquipmentInfo?> ResolveOneAsync(Exercise exercise, CancellationToken cancellationToken)
    {
        var map = await ResolveAsync([ExerciseNeed.Of(exercise)], cancellationToken);
        return map.GetValueOrDefault(exercise.Id);
    }

    /// <summary>Las máquinas del gym a las que está ligado el ejercicio: las ligas del gym mandan sobre las de modelo.</summary>
    public static IReadOnlyList<EquipmentItemInfo> CandidateMachines(
        IReadOnlyList<EquipmentItemInfo> inventory, IEnumerable<Guid> itemLinkIds, IEnumerable<Guid> modelLinkIds)
    {
        var direct = itemLinkIds.ToHashSet();
        var byItem = inventory.Where(i => direct.Contains(i.Id)).ToList();
        if (byItem.Count > 0)
        {
            return byItem;
        }

        var models = modelLinkIds.ToHashSet();
        return inventory.Where(i => i.ModelId is { } m && models.Contains(m)).ToList();
    }

    public static EquipmentMachineInfo ToMachine(EquipmentItemInfo item)
        => new(
            item.Id,
            item.Name,
            item.Type.ToString(),
            item.PhotoUrl,
            item.Zone,
            item.Instructions,
            item.Status.ToString(),
            item.ExpectedBackOn);
}
