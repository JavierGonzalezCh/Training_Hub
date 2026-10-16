using GSM.Application.Attendance.Abstractions;
using GSM.Application.Attendance.Contracts;
using GSM.Application.Attendance.Public;
using GSM.Application.Common;
using GSM.Application.Common.Abstractions;
using GSM.Application.Identity.Public;
using GSM.Application.Members.Public;
using GSM.Domain.Common.Exceptions;
using GSM.Domain.Identity;
using AttendanceEntity = GSM.Domain.Attendance.Attendance;

namespace GSM.Application.Attendance;

public sealed class ListAttendanceHandler(
    ICurrentUserService currentUser,
    IMemberReader members,
    IAttendanceRepository attendance,
    IIdentityReader identity)
{
    public async Task<PagedResult<AttendanceListItem>> HandleAsync(
        ListAttendanceQuery query, CancellationToken cancellationToken)
    {
        _ = currentUser.RequireGymId();

        var page = query.Page < 1 ? 1 : query.Page;
        var pageSize = Math.Clamp(query.PageSize, 1, 100);

        var (items, total) = await attendance.GetPageAsync(
            new AttendancePageQuery(query.MemberId, query.From, query.To, page, pageSize, query.AuthorizedOnly, query.IncludeVoided, query.DocumentOnly, query.NotCountedOnly),
            cancellationToken);

        var names = await ResolveNamesAsync(members, items.Select(a => a.MemberId), cancellationToken);
        var authorizers = await ResolveAuthorizersAsync(identity, items, cancellationToken);

        return new PagedResult<AttendanceListItem>(
            items.Select(a => ToItem(a, names.GetValueOrDefault(a.MemberId, "?"), authorizers)).ToList(),
            page, pageSize, total);
    }

    internal static AttendanceListItem ToItem(
        AttendanceEntity a, string memberName, IReadOnlyDictionary<Guid, string> authorizers)
        => new(
            a.Id, a.MemberId, memberName, a.CheckedInAt, a.AttendanceDate, a.Method.ToString(),
            Authorized: a.AuthorizedBy is not null,
            AuthorizedBy: a.AuthorizedBy,
            AuthorizedByName: a.AuthorizedBy is { } by ? authorizers.GetValueOrDefault(by) : null,
            AuthorizationReason: a.AuthorizationReason,
            Voided: a.IsVoided,
            VoidedAt: a.VoidedAt,
            VoidedBy: a.VoidedBy,
            VoidedByName: a.VoidedBy is { } vb ? authorizers.GetValueOrDefault(vb) : null,
            VoidReason: a.VoidReason,
            LocationCheck: a.LocationCheck?.ToString(),
            CountsForProgress: a.CountsForProgress);

    internal static async Task<IReadOnlyDictionary<Guid, string>> ResolveAuthorizersAsync(
        IIdentityReader identity, IEnumerable<AttendanceEntity> items, CancellationToken cancellationToken)
    {
        var names = new Dictionary<Guid, string>();
        foreach (var id in items.SelectMany(a => new[] { a.AuthorizedBy, a.VoidedBy }).Where(x => x.HasValue).Select(x => x!.Value).Distinct())
        {
            if (await identity.GetUserAsync(id, cancellationToken) is { } user)
            {
                names[id] = user.FullName;
            }
        }

        return names;
    }

    internal static async Task<Dictionary<Guid, string>> ResolveNamesAsync(
        IMemberReader members, IEnumerable<Guid> memberIds, CancellationToken cancellationToken)
    {
        var names = new Dictionary<Guid, string>();
        foreach (var id in memberIds.Distinct())
        {
            var m = await members.GetAsync(id, cancellationToken);
            names[id] = m is null ? "?" : $"{m.FirstName} {m.LastName}";
        }

        return names;
    }
}

public sealed class GetMemberAttendanceHandler(
    ICurrentUserService currentUser,
    IMemberReader members,
    IAttendanceRepository attendance,
    IIdentityReader identity)
{
    public async Task<PagedResult<AttendanceListItem>> HandleAsync(
        Guid memberId, int page, int pageSize, CancellationToken cancellationToken)
    {
        await MemberSelfAccess.EnsureAsync(currentUser, members, memberId, cancellationToken);

        var p = page < 1 ? 1 : page;
        var size = Math.Clamp(pageSize, 1, 100);

        var (items, total) = await attendance.GetPageAsync(
            new AttendancePageQuery(memberId, null, null, p, size), cancellationToken);

        var name = (await members.GetAsync(memberId, cancellationToken)) is { } m
            ? $"{m.FirstName} {m.LastName}" : "?";

        // El motivo de una entrada autorizada es interno del gym: el miembro no lo ve.
        var isStaff = currentUser.Role != UserRole.Member;
        var authorizers = isStaff
            ? await ListAttendanceHandler.ResolveAuthorizersAsync(identity, items, cancellationToken)
            : new Dictionary<Guid, string>();

        return new PagedResult<AttendanceListItem>(
            items.Select(a => isStaff
                ? ListAttendanceHandler.ToItem(a, name, authorizers)
                : new AttendanceListItem(
                    a.Id, a.MemberId, name, a.CheckedInAt, a.AttendanceDate, a.Method.ToString(),
                    LocationCheck: a.LocationCheck?.ToString(),
                    CountsForProgress: a.CountsForProgress)).ToList(),
            p, size, total);
    }
}

public sealed class GetPeakHoursHandler(
    ICurrentUserService currentUser, IClock clock, IAttendanceRepository attendance)
{
    /// <summary>Ventana por defecto cuando el cliente no acota el rango.</summary>
    private const int DefaultWindowDays = 30;

    public async Task<IReadOnlyList<PeakHourBucket>> HandleAsync(
        DateOnly? fromDate, DateOnly? toDate, CancellationToken cancellationToken)
    {
        _ = currentUser.RequireGymId();

        // Sin rango, los últimos 30 días hasta hoy. Antes eran DateOnly no anulables: al omitirlos
        // valían 0001-01-01 y la respuesta era un histograma de ceros con 200, indistinguible de
        // "no hubo asistencias".
        var to = toDate ?? clock.Today;
        var from = fromDate ?? to.AddDays(-(DefaultWindowDays - 1));

        if (to < from)
        {
            (from, to) = (to, from);
        }

        var fromUtc = clock.BusinessDayRange(from).Start;
        var toUtc = clock.BusinessDayRange(to).End;

        var instants = await attendance.GetCheckInInstantsAsync(fromUtc, toUtc, cancellationToken);

        var byHour = instants
            .GroupBy(i => clock.ToBusinessLocal(i).Hour)
            .ToDictionary(g => g.Key, g => g.Count());

        return Enumerable.Range(0, 24)
            .Select(h => new PeakHourBucket(h, byHour.GetValueOrDefault(h, 0)))
            .ToList();
    }
}

internal static class MemberSelfAccess
{
    public static async Task EnsureAsync(
        ICurrentUserService currentUser, IMemberReader members, Guid memberId, CancellationToken cancellationToken)
    {
        _ = currentUser.RequireGymId();

        if (currentUser.Role != UserRole.Member)
        {
            return;
        }

        var self = await members.ResolveByUserIdAsync(
            currentUser.UserId ?? Guid.Empty, cancellationToken);

        if (self is null || self.Id != memberId)
        {
            throw new ForbiddenDomainException("No puedes ver la actividad de otro miembro.");
        }
    }
}
