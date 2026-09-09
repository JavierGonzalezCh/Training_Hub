using GSM.Domain.Routines;

namespace GSM.Application.Routines.Abstractions;

/// <summary>Acceso a las entidades del módulo Routines. Sin <c>SaveChanges</c>.</summary>
public interface IRoutineRepository
{
    /// <summary>Rutina con sus días y líneas, rastreada (para editar/reemplazar).</summary>
    Task<Routine?> GetWithDaysAsync(Guid routineId, CancellationToken cancellationToken = default);

    Task<Routine?> GetActiveForMemberWithDaysAsync(Guid memberId, CancellationToken cancellationToken = default);

    /// <summary>Rutinas activas del socio (normalmente 0..1), sin días. Para dar de baja al reasignar.</summary>
    Task<IReadOnlyList<Routine>> GetActiveForMemberAsync(Guid memberId, CancellationToken cancellationToken = default);

    /// <summary>Cabeceras de todas las rutinas del socio, más reciente primero.</summary>
    Task<IReadOnlyList<Routine>> ListForMemberAsync(Guid memberId, CancellationToken cancellationToken = default);

    void Add(Routine routine);

    /// <summary>Marca para borrado los días (y líneas) actuales de la rutina. Para reemplazarlos en un PUT.</summary>
    void RemoveDaysOf(Routine routine);
}
