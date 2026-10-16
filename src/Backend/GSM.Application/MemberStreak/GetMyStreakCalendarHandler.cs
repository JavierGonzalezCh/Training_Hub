using GSM.Application.Achievements.Public;
using GSM.Application.Attendance.Public;
using GSM.Application.Common.Abstractions;
using GSM.Application.Members.Public;
using GSM.Application.Routines.Public;
using GSM.Domain.Common.Exceptions;

namespace GSM.Application.MemberStreak;

/// <summary>
/// L7: la racha del miembro autenticado explicada día a día para un mes ("Mi progreso" → Racha). Compositor como
/// <see cref="GetMemberStreakHandler"/>, del que toma la racha actual y los congeladores (consultar la racha puede
/// concederlos o consumirlos: es deliberado, no hay proceso diario). Lo demás es solo lectura: no guarda nada nuevo,
/// la mejor racha se recalcula sobre las asistencias.
/// </summary>
public sealed class GetMyStreakCalendarHandler(
    ICurrentUserService currentUser,
    IClock clock,
    IMemberReader members,
    IAttendanceReader attendance,
    IRoutineReader routines,
    IAchievementReader achievements,
    GetMemberStreakHandler streak)
{
    private const int MaxAvailableFreezes = 2;
    private const int GrantIntervalDays = 15;

    public async Task<StreakCalendarResponse> HandleAsync(int? year, int? month, CancellationToken cancellationToken)
    {
        _ = currentUser.RequireGymId();
        var self = await members.ResolveByUserIdAsync(currentUser.UserId ?? Guid.Empty, cancellationToken)
            ?? throw new ForbiddenDomainException("Tu cuenta no está vinculada a un miembro.");

        var today = clock.Today;
        var y = year ?? today.Year;
        var m = month ?? today.Month;
        if (y is < 2000 or > 2100 || m is < 1 or > 12)
        {
            throw new ValidationDomainException("El mes pedido no es válido.");
        }

        var monthStart = new DateOnly(y, m, 1);
        var monthEnd = monthStart.AddMonths(1).AddDays(-1);

        // La racha actual y los congeladores salen del compositor existente (concede/consume perezosamente).
        var current = await streak.HandleAsync(self.Id, cancellationToken);

        var activeStart = await routines.GetActiveRoutineStartAsync(self.Id, onlyAssigned: false, cancellationToken);
        var hasRoutine = activeStart is { } active && active <= today;
        var calendarStart = hasRoutine
            ? await routines.GetCalendarStartAsync(self.Id, onlyAssigned: false, cancellationToken)
            : null;

        var attendedAll = await attendance.GetAttendanceDatesAsync(self.Id, null, cancellationToken);
        var attended = attendedAll.ToHashSet();
        var freezes = await achievements.GetStreakFreezesAsync(self.Id, cancellationToken);
        var frozen = freezes.Where(f => f.UsedOn is not null).Select(f => f.UsedOn!.Value).ToHashSet();

        // Un solo calendario sirve al mes pedido, a hoy y a la mejor racha.
        var windowStart = calendarStart is { } cs && cs < monthStart ? cs : monthStart;

        IReadOnlySet<DateOnly> training = new HashSet<DateOnly>();
        IReadOnlySet<DateOnly> rest = new HashSet<DateOnly>();
        var windowEnd = today;
        if (windowStart <= windowEnd)
        {
            var calendar = await routines.GetTrainingCalendarAsync(
                self.Id, windowStart, windowEnd, onlyAssigned: false, cancellationToken);
            training = calendar.TrainingDays;
            rest = calendar.RestDays;
        }

        var uncounted = monthStart <= today
            ? (await attendance.GetUncountedEntryDaysAsync(
                self.Id, monthStart, monthEnd < today ? monthEnd : today, cancellationToken))
                .ToDictionary(d => d.Date)
            : new Dictionary<DateOnly, UncountedEntryDay>();

        var days = new List<StreakCalendarDay>();
        for (var day = monthStart; day <= monthEnd; day = day.AddDays(1))
        {
            var kind = "none";
            if (day <= today)
            {
                if (attended.Contains(day))
                {
                    kind = "trained";
                }
                else if (frozen.Contains(day))
                {
                    kind = "frozen";
                }
                else if (rest.Contains(day))
                {
                    kind = "rest";
                }
                else if (training.Contains(day))
                {
                    kind = day == today ? "pending" : "missed";
                }
            }

            string? note = null;
            if (uncounted.TryGetValue(day, out var entry) && kind != "trained")
            {
                note = entry.Outside
                    ? "Tu entrada de este día no contó: escaneaste el QR fuera del gimnasio."
                    : "Tu entrada de este día no contó: no compartiste tu ubicación.";
            }

            days.Add(new StreakCalendarDay(day, kind, note));
        }

        var todayKind = attended.Contains(today) ? "trained"
            : training.Contains(today) ? "train"
            : rest.Contains(today) ? "rest"
            : "free";

        var best = BestStreakCalculator.Compute(
            attendedAll, hasRoutine ? calendarStart : null, training, rest, frozen, today);
        best = Math.Max(best, current.CurrentStreak);

        DateOnly? nextFreeze = null;
        if (current.FreezesAvailable < MaxAvailableFreezes)
        {
            var last = freezes.Count == 0 ? (DateOnly?)null : freezes.Max(f => f.GrantedOn);
            nextFreeze = last is { } l ? l.AddDays(GrantIntervalDays) : today;
        }

        DateOnly? historyStart = null;
        if (attendedAll.Count > 0)
        {
            historyStart = attendedAll.Min();
        }

        if (hasRoutine && calendarStart is { } startOfCalendar && (historyStart is null || startOfCalendar < historyStart))
        {
            historyStart = startOfCalendar;
        }

        return new StreakCalendarResponse(
            y,
            m,
            current.CurrentStreak,
            best,
            todayKind,
            hasRoutine,
            current.FreezesAvailable,
            nextFreeze,
            frozen.OrderByDescending(d => d).ToList(),
            historyStart,
            days);
    }
}
