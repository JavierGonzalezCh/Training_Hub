using GSM.Application.Classes.Contracts;
using GSM.Application.Common.Abstractions;
using GSM.Application.Gyms;
using GSM.Application.Gyms.Public;
using GSM.Domain.Classes;
using GSM.Domain.Common.Exceptions;
using GSM.Domain.Gyms;

namespace GSM.Application.Classes;

/// <summary>
/// Reglas de Classes v2 (C1) sobre el horario del gym: una clase solo se agenda, se reserva y se
/// asiste dentro de un tramo de atención. Funciones puras sobre un <see cref="OpeningSchedule"/> que
/// Classes pide a Gyms por <c>IGymReader.GetOpeningScheduleAsync</c>. Un gym sin horario cargado
/// nunca rechaza nada.
/// </summary>
public static class ClassOpeningHours
{
    private static readonly string[] DayNames =
        ["lunes", "martes", "miércoles", "jueves", "viernes", "sábado", "domingo"];

    /// <summary>Día del horario semanal de Gyms que corresponde a un día de clase.</summary>
    public static OpeningDay ToOpeningDay(ClassDayOfWeek day) => (OpeningDay)(((int)day + 1) % 7);

    /// <summary>
    /// ¿Cabe <c>[start, start + duración]</c> entera dentro de los tramos del día? Los tramos que se
    /// tocan (cierra 12:00, abre 12:00) cuentan como uno solo. Una clase que cruza la medianoche no cabe.
    /// </summary>
    public static bool Fits(IReadOnlyList<TimeSlot> slots, TimeOnly start, int durationMinutes)
    {
        var cursor = Minutes(start);
        var end = cursor + durationMinutes;

        while (cursor < end)
        {
            var next = -1;
            foreach (var slot in slots)
            {
                if (Minutes(slot.Opens) <= cursor && cursor < slot.ClosesMinutes)
                {
                    next = Math.Max(next, slot.ClosesMinutes);
                }
            }

            if (next < 0)
            {
                return false;
            }

            cursor = next;
        }

        return true;
    }

    /// <summary>Una ocurrencia concreta: festivos y excepciones del gym mandan sobre la semana.</summary>
    public static bool OccurrenceFits(OpeningSchedule schedule, DateOnly date, TimeOnly start, int durationMinutes)
        => !schedule.HasSchedule || Fits(schedule.SlotsFor(date), start, durationMinutes);

    /// <summary>Días de la semana de una serie que no caben en el horario semanal (sin festivos).</summary>
    public static IReadOnlyList<ClassDayOfWeek> WeekdaysOutside(
        OpeningSchedule schedule, IEnumerable<ClassDayOfWeek> days, TimeOnly start, int durationMinutes)
        => schedule.HasSchedule
            ? days.Where(d => !Fits(schedule.SlotsForWeekday(ToOpeningDay(d)), start, durationMinutes)).ToList()
            : [];

    /// <summary>Marca como fuera de horario las ocurrencias no canceladas que no caben.</summary>
    public static IReadOnlyList<ScheduleEntry> Annotate(IEnumerable<ScheduleEntry> entries, OpeningSchedule schedule)
        => entries
            .Select(e => e.Status != ClassSessionStatus.Cancelled.ToString()
                && !OccurrenceFits(schedule, e.SessionDate, e.StartTime, e.DurationMinutes)
                    ? e with { OutsideOpeningHours = true }
                    : e)
            .ToList();

    public static string DayLabel(ClassDayOfWeek day) => DayNames[(int)day];

    /// <summary>"abre de 5:00 a 12:00 y de 15:00 a 22:00", o "no abre".</summary>
    public static string Describe(IReadOnlyList<TimeSlot> slots)
        => slots.Count == 0
            ? "no abre"
            : slots.Any(s => s.IsAllDay)
            ? "abre las 24 horas"
            : "abre " + string.Join(" y ", slots.OrderBy(s => s.Opens)
                .Select(s => $"de {OpenStatusText.Hour(s.Opens)} a {OpenStatusText.Hour(s.Closes)}"));

    /// <summary>
    /// Alta o edición: cada tramo de la clase debe caber en el horario del gym. Las recurrentes se miden
    /// contra el horario semanal de cada uno de sus días; las puntuales, contra su fecha (con festivos y
    /// excepciones). 422 si no caben.
    /// </summary>
    public static async Task EnsureDefinitionFitsAsync(
        IGymReader gyms, IClock clock, Guid gymId, SaveClassRequest request, CancellationToken cancellationToken)
    {
        var date = request.IsRecurring ? clock.Today : request.ScheduledDate ?? clock.Today;
        var schedule = await gyms.GetOpeningScheduleAsync(gymId, date, date, cancellationToken);
        if (!schedule.HasSchedule)
        {
            return;
        }

        var end = request.StartTime.AddMinutes(request.DurationMinutes);
        var span = $"{OpenStatusText.Hour(request.StartTime)} a {OpenStatusText.Hour(end)}";

        if (request.IsRecurring)
        {
            foreach (var day in (request.DaysOfWeek ?? []).Distinct())
            {
                var slots = schedule.SlotsForWeekday(ToOpeningDay(day));
                if (!Fits(slots, request.StartTime, request.DurationMinutes))
                {
                    throw new ValidationDomainException(
                        $"La clase ({span}) queda fuera del horario del gym el {DayLabel(day)}: {Describe(slots)}.");
                }
            }

            return;
        }

        var daySlots = schedule.SlotsFor(date);
        if (!Fits(daySlots, request.StartTime, request.DurationMinutes))
        {
            throw new ValidationDomainException(
                $"La clase ({span}) queda fuera del horario del gym el {date:dd/MM/yyyy}: {Describe(daySlots)}.");
        }
    }

    /// <summary>409 con el motivo si la ocurrencia no cabe en el horario del gym (reservar o marcar asistencia).</summary>
    public static async Task EnsureOccurrenceOpenAsync(
        IGymReader gyms, IClock clock, Guid gymId, DateOnly date, TimeOnly start, int durationMinutes,
        CancellationToken cancellationToken)
    {
        var schedule = await gyms.GetOpeningScheduleAsync(gymId, date, date, cancellationToken);
        if (OccurrenceFits(schedule, date, start, durationMinutes))
        {
            return;
        }

        var status = await gyms.GetOpenStatusAtLocalAsync(gymId, date, start, cancellationToken);
        throw new ConflictException(status.IsOpen
            ? "Gym cerrado: la clase termina después de la hora de cierre del gimnasio."
            : "Gym cerrado a la hora de esta clase. " + OpenStatusText.ClosedMessage(status, clock.Today));
    }

    private static int Minutes(TimeOnly t) => t.Hour * 60 + t.Minute;
}
