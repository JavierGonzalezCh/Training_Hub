using GSM.Domain.Routines;

namespace GSM.Application.Routines.Abstractions;

/// <summary>
/// Ligas de ejercicios con máquinas. Las de modelo son del sistema (sin gym); las de máquina son del gym actual
/// (el Global Query Filter ya las restringe). Sin <c>SaveChanges</c>.
/// </summary>
public interface IExerciseEquipmentRepository
{
    Task<IReadOnlyList<ExerciseEquipmentModel>> ListModelLinksAsync(
        IReadOnlyCollection<Guid> exerciseIds, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<ExerciseEquipmentItem>> ListItemLinksAsync(
        IReadOnlyCollection<Guid> exerciseIds, CancellationToken cancellationToken = default);

    /// <summary>Deja las ligas de modelo del ejercicio exactamente en <paramref name="modelIds"/> (idempotente).</summary>
    Task ReplaceModelLinksAsync(
        Guid exerciseId, IReadOnlyCollection<Guid> modelIds, CancellationToken cancellationToken = default);

    /// <summary>Deja las ligas de máquina del gym actual para el ejercicio exactamente en <paramref name="itemIds"/>.</summary>
    Task ReplaceItemLinksAsync(
        Guid gymId, Guid exerciseId, IReadOnlyCollection<Guid> itemIds, CancellationToken cancellationToken = default);
}
