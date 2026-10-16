using GSM.Domain.Routines;

namespace GSM.Application.Routines.Abstractions;

/// <summary>Acceso a las entidades del módulo Routines. Sin <c>SaveChanges</c>.</summary>
public interface IRoutineRepository
{
    /// <summary>Rutina con sus días y líneas, rastreada (para editar en el lugar). Solo trae días y líneas vigentes.</summary>
    Task<Routine?> GetWithDaysAsync(Guid routineId, CancellationToken cancellationToken = default);

    Task<Routine?> GetActiveForMemberWithDaysAsync(Guid memberId, CancellationToken cancellationToken = default);

    /// <summary>Rutinas activas del miembro (normalmente 0..1), sin días. Para dar de baja al reasignar.</summary>
    Task<IReadOnlyList<Routine>> GetActiveForMemberAsync(Guid memberId, CancellationToken cancellationToken = default);

    /// <summary>Cabeceras de todas las rutinas del miembro, más reciente primero.</summary>
    Task<IReadOnlyList<Routine>> ListForMemberAsync(Guid memberId, CancellationToken cancellationToken = default);

    void Add(Routine routine);

    /// <summary>
    /// Tras editar en el lugar: los días y líneas nuevos del agregado traen su id (UUID v7 del dominio) y EF
    /// los daría por existentes; esto los marca como nuevos para que se inserten.
    /// </summary>
    void TrackNewChildren(Routine routine);

    /// <summary>
    /// R10: vacía las notas (texto libre, pueden traer datos de salud) de las rutinas, las líneas y los registros de
    /// entrenamiento del miembro. Conserva todo lo demás. Escribe al instante, ignora el filtro de gym (corre sin
    /// usuario) y es idempotente.
    /// </summary>
    Task ClearNotesForMemberAsync(Guid gymId, Guid memberId, CancellationToken cancellationToken = default);
}
