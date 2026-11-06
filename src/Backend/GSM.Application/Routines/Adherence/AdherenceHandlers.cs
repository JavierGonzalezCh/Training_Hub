using GSM.Application.Common;
using GSM.Application.Common.Abstractions;
using GSM.Application.Gyms.Public;
using GSM.Application.Members.Public;
using GSM.Application.Routines.Abstractions;
using GSM.Application.Routines.Contracts;
using GSM.Application.Routines.Public;
using GSM.Domain.Common.Exceptions;
using Microsoft.Extensions.Logging;

namespace GSM.Application.Routines.Adherence;

/// <summary>Cumplimiento de un miembro en las últimas semanas; lo comparten la ficha, el listado y la app.</summary>
public sealed class MemberAdherenceService(IClock clock, IAdherenceSource source)
{
    public const int MaxWeeks = 26;

    public async Task<MemberAdherence> GetAsync(
        Guid memberId, int weeks, bool withDaysForCurrent, CancellationToken cancellationToken)
    {
        weeks = Math.Clamp(weeks, 1, MaxWeeks);
        var today = clock.Today;
        var last = AdherenceCalculator.WeekStartOf(today);
        var first = last.AddDays(-7 * (weeks - 1));

        var data = await source.LoadAsync([memberId], first, last.AddDays(6), cancellationToken);
        var items = AdherenceCalculator.Weeks(
            data.Routines, data.Logs, last, weeks, today, withDaysForCurrent, data.OverridesOrEmpty);
        return new MemberAdherence(memberId, data.Routines.Any(r => r.IsActive), items);
    }
}

/// <summary>Cumplimiento de la rutina de un miembro, para el staff (ficha del miembro, pestaña Rutina).</summary>
public sealed class GetMemberAdherenceHandler(
    ICurrentUserService currentUser,
    IMemberReader members,
    MemberAdherenceService adherence,
    ILogger<GetMemberAdherenceHandler> logger)
{
    public async Task<MemberAdherence> HandleAsync(Guid memberId, int weeks, CancellationToken cancellationToken)
    {
        _ = currentUser.RequireGymId();
        if (!await members.ExistsAsync(memberId, cancellationToken))
        {
            throw new NotFoundException("Miembro no encontrado.");
        }

        var result = await adherence.GetAsync(memberId, weeks, withDaysForCurrent: true, cancellationToken);
        logger.LogInformation(
            "Cumplimiento del miembro {MemberId} consultado por el staff: {Weeks} semanas", memberId, result.Weeks.Count);
        return result;
    }
}

/// <summary>La semana en curso del propio miembro ("Esta semana: 2 de 4 días"). 403 si el módulo de rutinas está apagado.</summary>
public sealed class GetMyAdherenceHandler(
    ICurrentUserService currentUser,
    IGymReader gyms,
    IMemberReader members,
    MemberAdherenceService adherence,
    ILogger<GetMyAdherenceHandler> logger)
{
    public async Task<MemberAdherence> HandleAsync(CancellationToken cancellationToken)
    {
        var memberId = await RoutineMemberContext.ResolveAsync(currentUser, gyms, members, cancellationToken);
        var result = await adherence.GetAsync(memberId, 1, withDaysForCurrent: true, cancellationToken);
        logger.LogInformation("Cumplimiento de la semana consultado por el miembro {MemberId}", memberId);
        return result;
    }
}

/// <summary>
/// Listado «Cumplimiento»: los miembros activos con una rutina activa, del que va más atrasado al que va mejor
/// (últimas 2 semanas: la pedida y la anterior). Los que aún no tienen días vencidos van al final. Se calcula al
/// leer, sin tabla propia.
/// </summary>
public sealed class ListRoutineAdherenceHandler(
    ICurrentUserService currentUser,
    IClock clock,
    IMemberReader members,
    IRoutineReader routineReader,
    IAdherenceSource source,
    ILogger<ListRoutineAdherenceHandler> logger)
{
    public const int DefaultPageSize = 25;
    public const int MaxPageSize = 100;

    public async Task<PagedResult<MemberAdherenceRow>> HandleAsync(
        DateOnly? weekStart, bool onlyBehind, int page, int pageSize, CancellationToken cancellationToken)
    {
        var gymId = currentUser.RequireGymId();
        page = Math.Max(1, page);
        pageSize = pageSize <= 0 ? DefaultPageSize : Math.Min(pageSize, MaxPageSize);

        var today = clock.Today;
        var week = AdherenceCalculator.WeekStartOf(weekStart is { } w && w <= today ? w : today);

        var active = await members.GetActiveMemberIdsAsync(gymId, cancellationToken);
        var withRoutine = (await routineReader.GetMemberIdsWithActiveRoutineAsync(active.ToList(), cancellationToken)).ToList();
        if (withRoutine.Count == 0)
        {
            return new PagedResult<MemberAdherenceRow>([], page, pageSize, 0);
        }

        var data = await source.LoadAsync(withRoutine, week.AddDays(-7), week.AddDays(6), cancellationToken);
        var lastLogs = await source.GetLastLogDatesAsync(withRoutine, cancellationToken);
        var names = await members.GetFullNamesAsync(withRoutine, cancellationToken);

        var routinesByMember = data.Routines.ToLookup(r => r.MemberId);
        var logsByMember = data.Logs.ToLookup(l => l.MemberId);
        var overridesByMember = data.OverridesOrEmpty.ToLookup(o => o.MemberId);

        var rows = new List<MemberAdherenceRow>(withRoutine.Count);
        foreach (var memberId in withRoutine)
        {
            var memberRoutines = routinesByMember[memberId].ToList();
            var current = memberRoutines
                .Where(r => r.IsActive)
                .OrderByDescending(r => r.StartsOn)
                .FirstOrDefault();
            if (current is null)
            {
                continue;
            }

            var weeks = AdherenceCalculator.Weeks(
                memberRoutines, logsByMember[memberId].ToList(), week, 2, today, withDaysForLast: false,
                overridesByMember[memberId].ToList());
            var thisWeek = weeks[1];
            var recent = AdherenceCalculator.Percent(weeks);

            if (onlyBehind && !(recent is { } r && r < AdherenceCalculator.BehindThresholdPercent))
            {
                continue;
            }

            rows.Add(new MemberAdherenceRow(
                memberId,
                names.GetValueOrDefault(memberId, "?"),
                current.Id,
                current.Name,
                current.SelfAssigned,
                week,
                thisWeek.PlannedDays,
                thisWeek.DoneDays,
                thisWeek.CompliancePercent,
                recent,
                lastLogs.TryGetValue(memberId, out var last) ? last : null));
        }

        var ordered = rows
            .OrderBy(r => r.RecentPercent is null)
            .ThenBy(r => r.RecentPercent)
            .ThenBy(r => r.FullName, StringComparer.OrdinalIgnoreCase)
            .ToList();

        logger.LogInformation(
            "Cumplimiento de rutinas consultado: semana {Week}, {Total} miembros, solo atrasados {OnlyBehind}",
            week, ordered.Count, onlyBehind);

        return new PagedResult<MemberAdherenceRow>(
            ordered.Skip((page - 1) * pageSize).Take(pageSize).ToList(), page, pageSize, ordered.Count);
    }
}
