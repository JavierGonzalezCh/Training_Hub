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

    /// <summary>Entradas consumidas si la membresía es una tiquetera (MS5); nulo si es por tiempo.</summary>
    public static async Task<int?> UsedAsync(
        IMembershipRepository memberships, Membership membership, CancellationToken cancellationToken)
        => membership.IsPass
            ? (await memberships.GetPassUsedCountsAsync([membership.Id], cancellationToken)).GetValueOrDefault(membership.Id)
            : null;

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
        return MembershipMapper.ToResponse(
            membership, planName, clock.Today, null, await MembershipLookup.UsedAsync(memberships, membership, cancellationToken));
    }
}

/// <summary>
/// Congela una membresía (MS4, §5.2): abierto desde hoy (sin fechas) o programado a futuro. Se valida
/// contra las reglas del plan; solo el Owner puede saltárselas. En uno programado el fin se corre ya
/// (y el de las encadenadas, en la misma transacción); en uno abierto, al reanudar.
/// </summary>
public sealed class CreateFreezeHandler(
    ICurrentUserService currentUser,
    IClock clock,
    IMembershipRepository memberships,
    IUnitOfWork unitOfWork,
    IDomainEventPublisher events,
    ILogger<CreateFreezeHandler> logger)
{
    public async Task<MembershipResponse> HandleAsync(
        Guid memberId, Guid membershipId, CreateFreezeRequest request, CancellationToken cancellationToken)
    {
        var gymId = currentUser.RequireGymId();
        var today = clock.Today;

        if (request.OverrideLimit
            && currentUser.Role != GSM.Domain.Identity.UserRole.Owner
            && !currentUser.IsImpersonating)
        {
            throw new ForbiddenDomainException("Solo el Owner puede superar el límite de congelamiento del plan.");
        }

        var membership = await MembershipLookup.RequireAsync(memberships, memberId, membershipId, cancellationToken);
        var plan = await memberships.GetPlanByIdAsync(membership.PlanId, cancellationToken)
            ?? throw new NotFoundException("Plan no encontrado.");
        var existing = await memberships.GetFreezesAsync([membership.Id], cancellationToken);

        var originalEnd = membership.EndDate;
        var (freeze, daysAdded) = membership.Freeze(
            plan, existing, today, request.StartsOn, request.EndsOn, request.Reason, request.OverrideLimit,
            currentUser.UserId);
        memberships.Add(freeze);
        await FreezeSpans.ShiftFollowingAsync(memberships, memberId, originalEnd, daysAdded, cancellationToken, membership.IsPass);
        await unitOfWork.SaveChangesAsync(cancellationToken);

        logger.LogInformation(
            "Membresía {MembershipId} del miembro {MemberId} congelada {Start}..{End}; fin corrido {Days} días",
            membership.Id, memberId, freeze.StartsOn, freeze.EndsOn, daysAdded);
        await events.PublishAsync(
            new MembershipFrozen(membership.Id, memberId, gymId, freeze.StartsOn, freeze.EndsOn, daysAdded),
            cancellationToken);

        var all = await memberships.GetFreezesAsync([membership.Id], cancellationToken);
        return MembershipMapper.ToResponse(
            membership, await MembershipLookup.PlanNameAsync(memberships, membership, cancellationToken), today, all,
            await MembershipLookup.UsedAsync(memberships, membership, cancellationToken));
    }
}

/// <summary>
/// Reanuda una membresía congelada (también antes de tiempo). Su fin se corre los días que estuvo
/// congelada (o se devuelven los que ya no se usan, si era programado) y, para que nunca haya dos
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
        var plan = await memberships.GetPlanByIdAsync(membership.PlanId, cancellationToken)
            ?? throw new NotFoundException("Plan no encontrado.");
        var freezes = await memberships.GetFreezesAsync([membership.Id], cancellationToken);

        var originalEnd = membership.EndDate;
        var ended = membership.EndFreeze(plan, freezes, today);
        if (ended.Removed)
        {
            memberships.Remove(ended.Freeze);
        }

        await FreezeSpans.ShiftFollowingAsync(memberships, memberId, originalEnd, ended.Delta, cancellationToken, membership.IsPass);
        await unitOfWork.SaveChangesAsync(cancellationToken);

        logger.LogInformation(
            "Membresía {MembershipId} del miembro {MemberId} reanudada; fin corrido {Days} días", membership.Id, memberId, ended.Delta);
        await events.PublishAsync(new MembershipResumed(membership.Id, memberId, gymId, ended.Delta), cancellationToken);

        var planName = await MembershipLookup.PlanNameAsync(memberships, membership, cancellationToken);
        var remaining = ended.Removed ? freezes.Where(f => f.Id != ended.Freeze.Id).ToList() : freezes;
        return MembershipMapper.ToResponse(
            membership, planName, today, remaining, await MembershipLookup.UsedAsync(memberships, membership, cancellationToken));
    }
}

/// <summary>Deshace un congelamiento programado que todavía no empieza: devuelve el fin y corre las encadenadas hacia atrás.</summary>
public sealed class DeleteFreezeHandler(
    ICurrentUserService currentUser,
    IClock clock,
    IMembershipRepository memberships,
    IUnitOfWork unitOfWork,
    IDomainEventPublisher events,
    ILogger<DeleteFreezeHandler> logger)
{
    public async Task<MembershipResponse> HandleAsync(
        Guid memberId, Guid membershipId, Guid freezeId, CancellationToken cancellationToken)
    {
        var gymId = currentUser.RequireGymId();
        var today = clock.Today;
        var membership = await MembershipLookup.RequireAsync(memberships, memberId, membershipId, cancellationToken);
        var freeze = await memberships.GetFreezeByIdAsync(freezeId, cancellationToken);
        if (freeze is null || freeze.MembershipId != membership.Id)
        {
            throw new NotFoundException("Congelamiento no encontrado.");
        }

        var originalEnd = membership.EndDate;
        var delta = membership.RemoveScheduledFreeze(freeze, today);
        memberships.Remove(freeze);
        await FreezeSpans.ShiftFollowingAsync(memberships, memberId, originalEnd, delta, cancellationToken, membership.IsPass);
        await unitOfWork.SaveChangesAsync(cancellationToken);

        logger.LogInformation("Congelamiento {FreezeId} de la membresía {MembershipId} deshecho", freezeId, membership.Id);
        await events.PublishAsync(new MembershipFreezeRemoved(membership.Id, memberId, gymId, -delta), cancellationToken);

        var remaining = (await memberships.GetFreezesAsync([membership.Id], cancellationToken))
            .Where(f => f.Id != freezeId).ToList();
        return MembershipMapper.ToResponse(
            membership, await MembershipLookup.PlanNameAsync(memberships, membership, cancellationToken), today, remaining,
            await MembershipLookup.UsedAsync(memberships, membership, cancellationToken));
    }
}

/// <summary>Historial de congelamientos de una membresía con lo que queda del límite del plan.</summary>
public sealed class ListFreezesHandler(
    ICurrentUserService currentUser,
    IClock clock,
    IMembershipRepository memberships)
{
    public async Task<MembershipFreezesResponse> HandleAsync(
        Guid memberId, Guid membershipId, CancellationToken cancellationToken)
    {
        _ = currentUser.RequireGymId();
        var today = clock.Today;
        var membership = await MembershipLookup.RequireAsync(memberships, memberId, membershipId, cancellationToken);
        var plan = await memberships.GetPlanByIdAsync(membership.PlanId, cancellationToken)
            ?? throw new NotFoundException("Plan no encontrado.");
        var freezes = await memberships.GetFreezesAsync([membership.Id], cancellationToken);

        var used = MembershipFreeze.DaysUsed(freezes);
        return new MembershipFreezesResponse(
            plan.MaxFreezeDays, plan.MinFreezeDays, used, Math.Max(0, plan.MaxFreezeDays - used),
            freezes.Select(f => MembershipMapper.ToResponse(f, today)).ToList());
    }
}
