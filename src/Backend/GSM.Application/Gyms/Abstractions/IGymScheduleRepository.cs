using GSM.Domain.Gyms;

namespace GSM.Application.Gyms.Abstractions;

/// <summary>Horario de atención del gym del token. Sin <c>SaveChanges</c>.</summary>
public interface IGymScheduleRepository
{
    /// <summary>Tramos del horario semanal (incluido el de festivos), ya rastreados para poder reemplazarlos.</summary>
    Task<IReadOnlyList<GymOpeningSlot>> GetWeeklySlotsAsync(CancellationToken cancellationToken = default);

    void Add(GymOpeningSlot slot);

    void Remove(GymOpeningSlot slot);

    Task<GymSpecialDay?> GetSpecialDayAsync(DateOnly date, CancellationToken cancellationToken = default);

    /// <summary>Excepciones con fecha en <c>[from, to]</c>, ordenadas por fecha.</summary>
    Task<IReadOnlyList<GymSpecialDay>> ListSpecialDaysAsync(
        DateOnly from, DateOnly to, CancellationToken cancellationToken = default);

    void Add(GymSpecialDay specialDay);

    void Remove(GymSpecialDay specialDay);
}
