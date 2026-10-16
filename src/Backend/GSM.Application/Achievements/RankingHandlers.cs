using GSM.Application.Achievements.Abstractions;
using GSM.Application.Achievements.Contracts;
using GSM.Application.Attendance.Public;
using GSM.Application.Common.Abstractions;
using GSM.Application.Gyms.Public;
using GSM.Application.Members.Public;
using GSM.Domain.Achievements;
using GSM.Domain.Common.Exceptions;
using GSM.Domain.Members;
using Microsoft.Extensions.Logging;

namespace GSM.Application.Achievements;

/// <summary>Una fila ya ordenada y numerada del ranking.</summary>
internal sealed record RankedRow(RankingParticipant Participant, MemberRankingView View, int Days, int Position);

/// <summary>Reglas comunes del ranking voluntario (L6, spec §5).</summary>
internal static class RankingRules
{
    public const int TopSize = 10;
    public const int PodiumSize = 3;

    /// <summary>
    /// 403 si el gym apagó el ranking (<c>ShowRanking</c>) o la gamificación entera (<c>ShowGamification</c>).
    /// Sin flags (dato faltante) se trata como apagado: ante la duda, privacidad.
    /// </summary>
    public static async Task RequireEnabledAsync(IGymReader gyms, Guid gymId, CancellationToken cancellationToken)
    {
        var flags = await gyms.GetFeatureFlagsAsync(gymId, cancellationToken);
        if (flags is null || !flags.ShowRanking || !flags.ShowGamification)
        {
            throw new ForbiddenDomainException("El ranking no está activo en este gimnasio.");
        }
    }

    public static async Task<MemberInfo> RequireSelfAsync(
        ICurrentUserService currentUser, IMemberReader members, CancellationToken cancellationToken)
        => await members.ResolveByUserIdAsync(currentUser.UserId ?? Guid.Empty, cancellationToken)
            ?? throw new ForbiddenDomainException("Tu cuenta no está vinculada a un miembro.");

    /// <summary>Motivo por el que el miembro no puede participar, o nulo si puede.</summary>
    public static string? CannotParticipateReason(MemberRankingView? view)
        => view is null ? "No encontramos tu ficha."
            : view.Status != MemberStatus.Active ? "Tu ficha está inactiva."
            : view.IsMinorOrUnknownAge ? "Los menores de edad no pueden aparecer en el ranking."
            : null;

    /// <summary>
    /// Ordena y numera: más días primero; a igualdad, quien llegó antes a esa cantidad (el último día con entrada
    /// más temprano). Solo cuentan participantes activos, mayores de edad y con al menos un día en el mes.
    /// </summary>
    public static IReadOnlyList<RankedRow> Rank(
        IReadOnlyList<RankingParticipant> participants,
        IReadOnlyDictionary<Guid, MemberRankingView> views,
        IReadOnlyList<MemberDaysInRange> counts)
    {
        var byMember = counts.ToDictionary(c => c.MemberId);
        return participants
            .Where(p => views.TryGetValue(p.MemberId, out var v) && CannotParticipateReason(v) is null)
            .Where(p => byMember.ContainsKey(p.MemberId))
            .Select(p => (Participant: p, View: views[p.MemberId], Count: byMember[p.MemberId]))
            .OrderByDescending(x => x.Count.Days)
            .ThenBy(x => x.Count.LastDay)
            .ThenBy(x => x.Participant.JoinedAt)
            .ThenBy(x => x.Participant.MemberId)
            .Select((x, i) => new RankedRow(x.Participant, x.View, x.Count.Days, i + 1))
            .ToList();
    }

    public static RankingEntryResponse ToEntry(RankedRow row, Guid selfMemberId)
        => new(
            row.Position,
            row.Participant.UseAlias
                ? row.Participant.Alias ?? string.Empty
                : RankingParticipant.RealDisplayName(row.View.FirstName, row.View.LastName),
            row.Participant.UseAlias ? null : row.View.PhotoUrl,
            row.Days,
            row.Participant.MemberId == selfMemberId);
}

/// <summary>
/// Ranking mensual de constancia del miembro (spec §5): top 10 del mes, su posición y el podio del mes anterior.
/// 403 si el gym lo apagó.
/// </summary>
public sealed class GetMyRankingHandler(
    ICurrentUserService currentUser,
    IClock clock,
    IGymReader gyms,
    IMemberReader members,
    IAttendanceReader attendance,
    IRankingRepository ranking,
    ILogger<GetMyRankingHandler> logger)
{
    public async Task<RankingResponse> HandleAsync(CancellationToken cancellationToken)
    {
        var gymId = currentUser.RequireGymId();
        await RankingRules.RequireEnabledAsync(gyms, gymId, cancellationToken);
        var self = await RankingRules.RequireSelfAsync(currentUser, members, cancellationToken);

        var today = clock.Today;
        var thisMonth = new DateOnly(today.Year, today.Month, 1);
        var previousMonth = thisMonth.AddMonths(-1);

        var participants = await ranking.ListAsync(cancellationToken);
        var ids = participants.Select(p => p.MemberId).Append(self.Id).Distinct().ToList();
        var views = await members.GetRankingViewsAsync(ids, today, cancellationToken);

        var participantIds = participants.Select(p => p.MemberId).ToList();
        var current = await attendance.GetDayCountsInRangeAsync(
            participantIds, thisMonth, thisMonth.AddMonths(1), cancellationToken);
        var previous = await attendance.GetDayCountsInRangeAsync(
            participantIds, previousMonth, thisMonth, cancellationToken);

        var ranked = RankingRules.Rank(participants, views, current);
        var podium = RankingRules.Rank(participants, views, previous).Take(RankingRules.PodiumSize);

        var mine = participants.FirstOrDefault(p => p.MemberId == self.Id);
        var myRow = ranked.FirstOrDefault(r => r.Participant.MemberId == self.Id);
        views.TryGetValue(self.Id, out var myView);
        var reason = RankingRules.CannotParticipateReason(myView);

        var me = new RankingMeResponse(
            Participating: mine is not null,
            UsesAlias: mine?.UseAlias ?? false,
            Alias: mine?.Alias,
            Position: myRow?.Position,
            Days: current.FirstOrDefault(c => c.MemberId == self.Id)?.Days ?? 0,
            CanParticipate: reason is null,
            CannotParticipateReason: reason);

        logger.LogInformation("Ranking consultado por el miembro {MemberId}", self.Id);

        return new RankingResponse(
            thisMonth.Year,
            thisMonth.Month,
            ranked.Take(RankingRules.TopSize).Select(r => RankingRules.ToEntry(r, self.Id)).ToList(),
            me,
            previousMonth.Year,
            previousMonth.Month,
            podium.Select(r => RankingRules.ToEntry(r, self.Id)).ToList());
    }
}

/// <summary>
/// El miembro acepta aparecer en el ranking, o cambia cómo se le ve (PUT: idempotente). 403 si el gym lo apagó;
/// 422 si es menor de edad, no tiene fecha de nacimiento o el alias no es válido; 409 si otro ya usa ese alias.
/// </summary>
public sealed class JoinRankingHandler(
    ICurrentUserService currentUser,
    IClock clock,
    IGymReader gyms,
    IMemberReader members,
    IRankingRepository ranking,
    IUnitOfWork unitOfWork,
    ILogger<JoinRankingHandler> logger)
{
    public async Task<RankingMeResponse> HandleAsync(JoinRankingRequest request, CancellationToken cancellationToken)
    {
        var gymId = currentUser.RequireGymId();
        await RankingRules.RequireEnabledAsync(gyms, gymId, cancellationToken);
        var self = await RankingRules.RequireSelfAsync(currentUser, members, cancellationToken);

        var views = await members.GetRankingViewsAsync([self.Id], clock.Today, cancellationToken);
        views.TryGetValue(self.Id, out var view);
        if (RankingRules.CannotParticipateReason(view) is { } reason)
        {
            throw new ValidationDomainException(reason);
        }

        var alias = request.UseAlias ? RankingParticipant.NormalizeAlias(request.Alias) : null;
        if (alias is not null && await ranking.AliasTakenAsync(alias, self.Id, cancellationToken))
        {
            throw new ConflictException("Otro participante ya usa ese alias. Elige otro.");
        }

        var existing = await ranking.GetByMemberAsync(self.Id, cancellationToken);
        if (existing is null)
        {
            existing = RankingParticipant.Join(gymId, self.Id, request.UseAlias, alias, clock.UtcNow);
            ranking.Add(existing);
        }
        else
        {
            existing.SetDisplay(request.UseAlias, alias);
        }

        await unitOfWork.SaveChangesAsync(cancellationToken);
        logger.LogInformation(
            "Miembro {MemberId} participa en el ranking (con alias: {UsesAlias})", self.Id, existing.UseAlias);

        return new RankingMeResponse(true, existing.UseAlias, existing.Alias, null, 0, true, null);
    }
}

/// <summary>El miembro deja de aparecer en el ranking. Borra la fila; sin participación es un no-op.</summary>
public sealed class LeaveRankingHandler(
    ICurrentUserService currentUser,
    IMemberReader members,
    IRankingRepository ranking,
    IUnitOfWork unitOfWork,
    ILogger<LeaveRankingHandler> logger)
{
    public async Task HandleAsync(CancellationToken cancellationToken)
    {
        _ = currentUser.RequireGymId();
        var self = await RankingRules.RequireSelfAsync(currentUser, members, cancellationToken);

        var existing = await ranking.GetByMemberAsync(self.Id, cancellationToken);
        if (existing is null)
        {
            return;
        }

        ranking.Remove(existing);
        await unitOfWork.SaveChangesAsync(cancellationToken);
        logger.LogInformation("Miembro {MemberId} salió del ranking", self.Id);
    }
}
