using GSM.Application.Attendance.Abstractions;
using GSM.Application.Attendance.Contracts;
using GSM.Application.Common.Abstractions;
using GSM.Application.Gyms;
using GSM.Application.Gyms.Public;
using GSM.Application.Members.Public;
using GSM.Application.Memberships.Public;
using GSM.Domain.Common.Exceptions;
using GSM.Domain.Members;

namespace GSM.Application.Attendance;

/// <summary>
/// Paso 1 de la recepción (Attendance v2, §4): la tarjeta del miembro antes de registrar nada.
/// Solo lectura: junta el veredicto de Memberships, si ya entró hoy, la exoneración y el horario.
/// </summary>
public sealed class GetReceptionCardHandler(
    ICurrentUserService currentUser,
    IClock clock,
    IMemberReader members,
    IMembershipReader memberships,
    IGymReader gyms,
    IAttendanceRepository attendance,
    IMemberEntryRuleRepository entryRules)
{
    public async Task<ReceptionCardResponse> HandleAsync(Guid memberId, CancellationToken cancellationToken)
    {
        var gymId = currentUser.RequireGymId();
        var member = await members.GetAsync(memberId, cancellationToken)
            ?? throw new NotFoundException("Miembro no encontrado.");

        var access = await memberships.GetAccessStatusAsync(memberId, cancellationToken);
        var entriesToday = await attendance.GetEntriesOnDateAsync(memberId, clock.Today, cancellationToken);
        var rule = await entryRules.GetForMemberAsync(memberId, cancellationToken);
        var allowance = EntryAllowance.Evaluate(entriesToday, rule, clock.Today, clock.UtcNow);
        var open = await gyms.GetOpenStatusAsync(gymId, clock.UtcNow, cancellationToken);
        var missingWaiver = !await members.HasLiabilityWaiverAsync(memberId, cancellationToken);

        var active = member.Status == MemberStatus.Active;
        var closedMessage = open.IsOpen
            ? null
            : OpenStatusText.ClosedMessage(open, DateOnly.FromDateTime(clock.ToBusinessLocal(clock.UtcNow)));

        // Mismo orden que CheckInHandler (§3): cerrado, de baja, ya entró, membresía.
        string? rejection = null;
        if (!open.IsOpen)
        {
            rejection = "gym_closed";
        }
        else if (!active)
        {
            rejection = "member_inactive";
        }
        else if (!allowance.Allowed)
        {
            rejection = "already_checked_in";
        }
        else if (access.Kind == AccessKind.Blocked)
        {
            rejection = access.BlockReason switch
            {
                AccessBlockReason.Frozen => "membership_frozen",
                AccessBlockReason.Expired => "membership_expired",
                _ => "no_membership",
            };
        }

        var blockedByMembership = rejection is "membership_frozen" or "membership_expired" or "no_membership";

        return new ReceptionCardResponse(
            member.Id,
            $"{member.FirstName} {member.LastName}",
            member.PhotoUrl,
            active,
            EnumText.Snake(access.Commercial)!,
            EnumText.Snake(access.Kind)!,
            access.EndsOn,
            access.DaysLeft,
            access.GraceDaysLeft,
            access.Notice,
            access.BlockMessage,
            allowance.EntriesToday > 0,
            allowance.LastEntryAt,
            missingWaiver,
            !open.IsOpen,
            closedMessage,
            CanRegister: rejection is null,
            CanLetThrough: blockedByMembership,
            RejectionReason: rejection,
            EntriesToday: allowance.EntriesToday,
            MaxDailyEntries: allowance.AllowedToday,
            MultipleEntries: allowance.MultipleEntries,
            NextEntryAt: allowance.NextEntryAt);
    }
}
