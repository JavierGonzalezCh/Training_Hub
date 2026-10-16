using GSM.Application.Common.Abstractions;
using GSM.Application.Members.Public;
using GSM.Application.Memberships.Abstractions;
using GSM.Application.Memberships.Contracts;
using GSM.Application.Memberships.Public;
using GSM.Domain.Common.Exceptions;

namespace GSM.Application.Memberships;

/// <summary>
/// MS8: "Mi membresía" del miembro autenticado. Su veredicto de hoy y el historial de sus membresías con los
/// congelamientos y las entradas de tiquetera que consumió. Solo lectura y solo la suya: la identidad sale del token.
/// </summary>
public sealed class GetMyMembershipsHandler(
    ICurrentUserService currentUser,
    IClock clock,
    IMemberReader members,
    IMembershipReader membershipReader,
    IMembershipRepository memberships)
{
    public async Task<MyMembershipsResponse> HandleAsync(CancellationToken cancellationToken)
    {
        _ = currentUser.RequireGymId();
        var self = await members.ResolveByUserIdAsync(currentUser.UserId ?? Guid.Empty, cancellationToken)
            ?? throw new ForbiddenDomainException("Tu cuenta no está vinculada a un miembro.");

        var today = clock.Today;
        var access = MembershipMapper.ToResponse(await membershipReader.GetAccessStatusAsync(self.Id, cancellationToken));

        var history = await memberships.GetHistoryAsync(self.Id, cancellationToken);
        var ids = history.Select(m => m.Id).ToList();
        var planNames = await memberships.GetPlanNamesAsync(
            history.Select(m => m.PlanId).Distinct().ToList(), cancellationToken);
        var freezes = await memberships.GetFreezesAsync(ids, cancellationToken);
        var usages = await memberships.GetPassUsagesAsync(
            history.Where(m => m.IsPass).Select(m => m.Id).ToList(), cancellationToken);

        var items = history
            .OrderByDescending(m => m.StartDate).ThenByDescending(m => m.CreatedAt)
            .Select(m =>
            {
                var own = usages.Where(u => u.MembershipId == m.Id).ToList();
                // Mismo cálculo de estado y saldo que la respuesta del staff, sin el dinero.
                var full = MembershipMapper.ToResponse(
                    m, planNames.GetValueOrDefault(m.PlanId, "(plan eliminado)"), today, freezes,
                    m.IsPass ? own.Count : null);
                return new MyMembershipItem(
                    m.Id,
                    full.PlanName,
                    m.IsPass ? "pass" : "time",
                    full.Status,
                    m.StartDate,
                    m.EndDate,
                    full.EntriesTotal,
                    full.EntriesUsed,
                    full.EntriesLeft,
                    full.Status == "Active" && m.StartDate > today,
                    m.GroupId is not null,
                    freezes.Where(f => f.MembershipId == m.Id)
                        .OrderBy(f => f.StartsOn)
                        .Select(f => new MyFreezeItem(
                            f.StartsOn, f.EndsOn, f.Reason, MembershipMapper.ToResponse(f, today).State, f.DaysCredited))
                        .ToList(),
                    own.OrderBy(u => u.UsedOn).Select(u => new MyPassEntryItem(u.UsedOn)).ToList());
            })
            .ToList();

        return new MyMembershipsResponse(access, items);
    }
}
