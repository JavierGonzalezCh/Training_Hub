using GSM.Application.Common.Abstractions;
using GSM.Application.Gyms.Public;
using GSM.Application.Members.Public;
using GSM.Application.Routines.Abstractions;
using GSM.Domain.Common.Exceptions;
using GSM.Domain.Routines;
using Microsoft.Extensions.Logging;

namespace GSM.Application.Routines;

/// <summary>
/// Una fecha de la semana con lo que toca en ella según <see cref="RoutineCalendar"/> (con los cambios del miembro).
/// <c>DayId</c>/<c>DayName</c> son nulos en un descanso sin fila o sin rutina vigente. <c>PlannedDayName</c> trae el día
/// que tocaba si el miembro lo cambió. <c>Done</c>: hay registros de las líneas de ese día en esa fecha.
/// </summary>
public sealed record RoutineWeekDayView(
    DateOnly Date,
    int DayOfWeek,
    bool IsRest,
    Guid? DayId,
    string? DayName,
    string? PlannedDayName,
    bool Done,
    bool IsToday);

/// <summary>La semana (lunes a domingo) que contiene la fecha pedida. <c>HasRoutine</c> = alguna fecha tiene rutina vigente.</summary>
public sealed record MyRoutineWeekResponse(DateOnly WeekStart, bool HasRoutine, IReadOnlyList<RoutineWeekDayView> Days);

/// <summary>
/// Planificador de la app del miembro: qué toca cada día de una semana, para navegar entre semanas. Se calcula al
/// leer con la misma regla que la racha y el cumplimiento; no escribe nada.
/// </summary>
public sealed class GetMyRoutineWeekHandler(
    ICurrentUserService currentUser,
    IClock clock,
    IGymReader gyms,
    IMemberReader members,
    IAdherenceSource source,
    ILogger<GetMyRoutineWeekHandler> logger)
{
    /// <summary>Máximo de semanas hacia atrás o hacia adelante desde hoy.</summary>
    public const int MaxWeeksAway = 26;

    public async Task<MyRoutineWeekResponse> HandleAsync(DateOnly? date, CancellationToken cancellationToken)
    {
        var memberId = await RoutineMemberContext.ResolveAsync(currentUser, gyms, members, cancellationToken);
        var today = clock.Today;
        var start = MondayOf(date ?? today);
        if (Math.Abs(start.DayNumber - MondayOf(today).DayNumber) > MaxWeeksAway * 7)
        {
            throw new ValidationDomainException($"Solo puedes ver hasta {MaxWeeksAway} semanas antes o después de hoy.");
        }

        var end = start.AddDays(6);
        var data = await source.LoadAsync([memberId], start, end, cancellationToken);
        var ordered = RoutineCalendar.Order(data.Routines);
        var overrides = RoutineCalendar.ByDate(data.OverridesOrEmpty);
        var logged = data.Logs.Select(l => (l.PerformedOn, l.RoutineExerciseId)).ToHashSet();

        var days = new List<RoutineWeekDayView>(7);
        for (var d = start; d <= end; d = d.AddDays(1))
        {
            var resolution = RoutineCalendar.Resolve(ordered, d, overrides);
            var day = resolution.Day;
            var isRest = day is null || day.IsRest;
            var done = day is not null && day.Lines.Any(line => logged.Contains((d, line.Id)));
            days.Add(new RoutineWeekDayView(
                d,
                IsoWeekDay.Of(d),
                isRest,
                isRest ? null : day!.Id,
                isRest ? null : day!.Name,
                resolution.PlannedDay?.Name,
                done,
                d == today));
        }

        logger.LogInformation("Semana de rutina {WeekStart} consultada por el miembro {MemberId}", start, memberId);
        return new MyRoutineWeekResponse(start, ordered.Count > 0, days);
    }

    public static DateOnly MondayOf(DateOnly date) => date.AddDays(1 - IsoWeekDay.Of(date));
}
