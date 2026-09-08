using GSM.Application.Common.Abstractions;
using GSM.Application.Members.Public;
using GSM.Application.Memberships.Abstractions;
using GSM.Application.Memberships.Contracts;

namespace GSM.Application.Memberships;

/// <summary>Membresías del gym que vencen dentro de <c>days</c> días (para que recepción llame a renovar).</summary>
public sealed class GetExpiringMembershipsHandler(
    ICurrentUserService currentUser,
    IMemberReader members,
    IMembershipRepository memberships)
{
    public async Task<IReadOnlyList<ExpiringMembershipItem>> HandleAsync(
        int withinDays, CancellationToken cancellationToken)
    {
        var gymId = currentUser.RequireGymId();
        var today = DateOnly.FromDateTime(DateTime.UtcNow);
        var days = Math.Clamp(withinDays, 1, 90);

        var rows = await memberships.GetExpiringAsync(gymId, today, days, cancellationToken);

        var result = new List<ExpiringMembershipItem>(rows.Count);
        foreach (var row in rows)
        {
            var member = await members.GetAsync(row.MemberId, cancellationToken);
            var name = member is null ? "(miembro no encontrado)" : $"{member.FirstName} {member.LastName}";
            result.Add(new ExpiringMembershipItem(
                row.MembershipId, row.MemberId, name, row.PlanName, row.EndDate,
                row.EndDate.DayNumber - today.DayNumber));
        }

        return result;
    }
}
