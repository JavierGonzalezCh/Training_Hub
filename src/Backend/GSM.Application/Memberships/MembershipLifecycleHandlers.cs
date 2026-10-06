using GSM.Application.Common.Abstractions;
using GSM.Application.Common.Events;
using GSM.Application.Memberships.Abstractions;
using GSM.Application.Memberships.Contracts;
using GSM.Application.Memberships.Events;
using GSM.Domain.Common.Exceptions;
using GSM.Domain.Memberships;
using Microsoft.Extensions.Logging;

namespace GSM.Application.Memberships;

/// <summary>Carga la membresía de un miembro concreto (404 si no existe o es de otro miembro).</summary>
internal static class MembershipLookup
{
    public static async Task<Membership> RequireAsync(
        IMembershipRepository memberships, Guid memberId, Guid membershipId, CancellationToken cancellationToken)
    {
        var membership = await memberships.GetByIdAsync(membershipId, cancellationToken);
        return membership is not null && membership.MemberId == memberId
            ? membership
            : throw new NotFoundException("Membresía no encontrada.");
    }

    public static async Task<string> PlanNameAsync(
        IMembershipRepository memberships, Membership membership, CancellationToken cancellationToken)
        => (await memberships.GetPlanByIdAsync(membership.PlanId, cancellationToken))?.Name ?? string.Empty;
}

/// <summary>
/// Cancela una membresía asignada por error. Queda en el historial y deja de cubrir fechas. Los
/// pagos enlazados no se tocan aquí (son de Payments): la UI ofrece anularlos en el mismo flujo.
/// </summary>
public sealed class CancelMembershipHandler(
    ICurrentUserService currentUser,
    IClock clock,
    IMembershipRepository memberships,
    IUnitOfWork unitOfWork,
    IDomainEventPublisher events,
    ILogger<CancelMembershipHandler> logger)
{
    public async Task<MembershipResponse> HandleAsync(
        Guid memberId, Guid membershipId, CancelMembershipRequest request, CancellationToken cancellationToken)
    {
        var gymId = currentUser.RequireGymId();
        var membership = await MembershipLookup.RequireAsync(memberships, memberId, membershipId, cancellationToken);

        membership.Cancel(request.Reason, clock.UtcNow);
        await unitOfWork.SaveChangesAsync(cancellationToken);

        logger.LogInformation("Membresía {MembershipId} del miembro {MemberId} cancelada", membership.Id, memberId);
        await events.PublishAsync(new MembershipCancelled(membership.Id, memberId, gymId), cancellationToken);

        var planName = await MembershipLookup.PlanNameAsync(memberships, membership, cancellationToken);
        return MembershipMapper.ToResponse(membership, planName, clock.Today);
    }
}

/// <summary>Congela la membresía vigente hoy: mientras tanto el miembro no tiene acceso por QR.</summary>
public sealed class PauseMembershipHandler(
    ICurrentUserService currentUser,
    IClock clock,
    IMembershipRepository memberships,
    IUnitOfWork unitOfWork,
    IDomainEventPublisher events,
    ILogger<PauseMembershipHandler> logger)
{
    public async Task<MembershipResponse> HandleAsync(
        Guid memberId, Guid membershipId, CancellationToken cancellationToken)
    {
        var gymId = currentUser.RequireGymId();
        var today = clock.Today;
        var membership = await MembershipLookup.RequireAsync(memberships, memberId, membershipId, cancellationToken);

        membership.Pause(today);
        await unitOfWork.SaveChangesAsync(cancellationToken);

        logger.LogInformation("Membresía {MembershipId} del miembro {MemberId} congelada el {Today}", membership.Id, memberId, today);
        await events.PublishAsync(new MembershipPaused(membership.Id, memberId, gymId, today), cancellationToken);

        var planName = await MembershipLookup.PlanNameAsync(memberships, membership, cancellationToken);
        return MembershipMapper.ToResponse(membership, planName, today);
    }
}

/// <summary>
/// Reanuda una membresía congelada. Su fin se corre los días congelados y, para que nunca haya dos
/// membresías cubriendo la misma fecha, las encadenadas detrás se corren lo mismo en la misma transacción.
/// </summary>
public sealed class ResumeMembershipHandler(
    ICurrentUserService currentUser,
    IClock clock,
    IMembershipRepository memberships,
    IUnitOfWork unitOfWork,
    IDomainEventPublisher events,
    ILogger<ResumeMembershipHandler> logger)
{
    public async Task<MembershipResponse> HandleAsync(
        Guid memberId, Guid membershipId, CancellationToken cancellationToken)
    {
        var gymId = currentUser.RequireGymId();
        var today = clock.Today;
        var membership = await MembershipLookup.RequireAsync(memberships, memberId, membershipId, cancellationToken);

        var originalEnd = membership.EndDate;
        var days = membership.Resume(today);

        if (days > 0)
        {
            foreach (var following in await memberships.GetActiveStartingAfterAsync(memberId, originalEnd, cancellationToken))
            {
                following.ShiftBy(days);
            }
        }

        await unitOfWork.SaveChangesAsync(cancellationToken);

        logger.LogInformation(
            "Membresía {MembershipId} del miembro {MemberId} reanudada; fin corrido {Days} días", membership.Id, memberId, days);
        await events.PublishAsync(new MembershipResumed(membership.Id, memberId, gymId, days), cancellationToken);

        var planName = await MembershipLookup.PlanNameAsync(memberships, membership, cancellationToken);
        return MembershipMapper.ToResponse(membership, planName, today);
    }
}
