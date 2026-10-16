using GSM.Domain.Routines;

namespace GSM.Application.Routines.Abstractions;

/// <summary>Plantillas de rutina del gym (R11). Sin <c>SaveChanges</c>; el filtro de gym lo pone el Global Query Filter.</summary>
public interface IRoutineTemplateRepository
{
    Task<RoutineTemplate?> GetAsync(Guid id, CancellationToken cancellationToken = default);

    /// <summary>Todas las del gym, por nombre.</summary>
    Task<IReadOnlyList<RoutineTemplate>> ListAsync(CancellationToken cancellationToken = default);

    void Add(RoutineTemplate template);

    void Remove(RoutineTemplate template);
}
