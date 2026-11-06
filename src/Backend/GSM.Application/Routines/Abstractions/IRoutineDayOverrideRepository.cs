using GSM.Domain.Routines;

namespace GSM.Application.Routines.Abstractions;

/// <summary>Ajustes del calendario de un miembro (SP, docs/GSM-sesion-perdida.md).</summary>
public interface IRoutineDayOverrideRepository
{
    /// <summary>Los ajustes del miembro entre <paramref name="from"/> y <paramref name="to"/>, rastreados.</summary>
    Task<IReadOnlyList<RoutineDayOverride>> ListAsync(
        Guid memberId, DateOnly from, DateOnly to, CancellationToken cancellationToken = default);

    /// <summary>Cuántas veces se corrió el ciclo de la rutina hasta <paramref name="upTo"/> (incluida).</summary>
    Task<int> CountShiftsAsync(
        Guid memberId, Guid routineId, DateOnly upTo, CancellationToken cancellationToken = default);

    void Add(RoutineDayOverride change);

    void Remove(RoutineDayOverride change);
}
