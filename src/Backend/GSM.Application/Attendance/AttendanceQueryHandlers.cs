using GSM.Application.Attendance.Abstractions;
using GSM.Application.Attendance.Contracts;
using GSM.Application.Attendance.Public;
using GSM.Application.Common;
using GSM.Application.Common.Abstractions;
using GSM.Application.Members.Public;
using GSM.Domain.Common.Exceptions;
using GSM.Domain.Identity;

namespace GSM.Application.Attendance;

public sealed class ListAttendanceHandler(
    ICurrentUserService currentUser, IMemberReader members, IAttendanceRepository attendance)
{
    public async Task<PagedResult<AttendanceListItem>> HandleAsync(
        ListAttendanceQuery query, CancellationToken cancellationToken)
    {
        _ = currentUser.RequireGymId();

        var page = query.Page < 1 ? 1 : query.Page;
        var pageSize = Math.Clamp(query.PageSize, 1, 100);

        var (items, total) = await attendance.GetPageAsync(
            new AttendancePageQuery(query.MemberId, query.From, query.To, page, pageSize), cancellationToken);

        var names = await ResolveNamesAsync(members, items.Select(a => a.MemberId), cancellationToken);

        return new PagedResult<AttendanceListItem>(
            items.Select(a => new AttendanceListItem(
                a.Id, a.MemberId, names.GetValueOrDefault(a.MemberId, "?"),
                a.CheckedInAt, a.AttendanceDate, a.Method.ToString())).ToList(),
            page, pageSize, total);
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
    ICurrentUserService currentUser, IMemberReader members, IAttendanceRepository attendance)
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

        return new PagedResult<AttendanceListItem>(
            items.Select(a => new AttendanceListItem(
                a.Id, a.MemberId, name, a.CheckedInAt, a.AttendanceDate, a.Method.ToString())).ToList(),
            p, size, total);
    }
}

public sealed class GetMemberStreakHandler(
    ICurrentUserService currentUser,
    IMemberReader members,
    IAttendanceReader attendanceReader)
{
    public async Task<MemberStreakResponse> HandleAsync(Guid memberId, CancellationToken cancellationToken)
    {
        await MemberSelfAccess.EnsureAsync(currentUser, members, memberId, cancellationToken);

        return new MemberStreakResponse(
            memberId,
            await attendanceReader.GetStreakAsync(memberId, cancellationToken),
            await attendanceReader.GetVisitCountAsync(memberId, cancellationToken),
            await attendanceReader.GetLastVisitAsync(memberId, cancellationToken));
    }
}

public sealed class GetPeakHoursHandler(
    ICurrentUserService currentUser, IClock clock, IAttendanceRepository attendance)
{
    public async Task<IReadOnlyList<PeakHourBucket>> HandleAsync(
        DateOnly from, DateOnly to, CancellationToken cancellationToken)
    {
        _ = currentUser.RequireGymId();

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
