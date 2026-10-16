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

    /// <summary>
    /// Las membresías que se mueven juntas con <paramref name="membership"/> (MS7, §9): las no canceladas de su grupo,
    /// incluida ella misma. Una membresía individual se mueve sola.
    /// </summary>
    public static async Task<IReadOnlyList<Membership>> GroupActiveAsync(
        IMembershipRepository memberships, Membership membership, CancellationToken cancellationToken)
    {
        if (membership.GroupId is not { } groupId)
        {
            return [membership];
        }

        var all = await memberships.GetByGroupAsync(groupId, cancellationToken);
        var moving = all.Where(m => m.Status == MembershipStatus.Active && m.Id != membership.Id).ToList();
        moving.Insert(0, membership);
        return moving;
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

        // MS7 (§9): cancelar la membresía del titular cancela el grupo entero; cancelar la de un beneficiario es sacarlo
        // del grupo (libera su cupo) y no toca a los demás.
        var cancelled = new List<Membership> { membership };
        membership.Cancel(request.Reason, clock.UtcNow);
        if (membership.IsGroupHolder)
        {
            foreach (var sibling in (await MembershipLookup.GroupActiveAsync(memberships, membership, cancellationToken))
                .Where(m => m.Id != membership.Id))
            {
                sibling.Cancel(request.Reason, clock.UtcNow);
                cancelled.Add(sibling);
            }
        }

        await unitOfWork.SaveChangesAsync(cancellationToken);

        logger.LogInformation(
            "Membresía {MembershipId} del miembro {MemberId} cancelada ({Count} en total)", membership.Id, memberId, cancelled.Count);
        foreach (var item in cancelled)
        {
            await events.PublishAsync(new MembershipCancelled(item.Id, item.MemberId, gymId), cancellationToken);
        }

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

        // MS7 (§9): un grupo se congela junto. Cada integrante valida el límite del plan con su propio historial y
        // corre sus propias membresías encadenadas; si uno no cabe, no se congela ninguno.
        var frozen = new List<(Membership Membership, MembershipFreeze Freeze, int DaysAdded)>();
        foreach (var moving in await MembershipLookup.GroupActiveAsync(memberships, membership, cancellationToken))
        {
            var existing = await memberships.GetFreezesAsync([moving.Id], cancellationToken);
            var originalEnd = moving.EndDate;
            var (freeze, daysAdded) = moving.Freeze(
                plan, existing, today, request.StartsOn, request.EndsOn, request.Reason, request.OverrideLimit,
                currentUser.UserId);
            memberships.Add(freeze);
            await FreezeSpans.ShiftFollowingAsync(memberships, moving.MemberId, originalEnd, daysAdded, cancellationToken, moving.IsPass);
            frozen.Add((moving, freeze, daysAdded));
        }

        await unitOfWork.SaveChangesAsync(cancellationToken);

        foreach (var (moving, freeze, daysAdded) in frozen)
        {
            logger.LogInformation(
                "Membresía {MembershipId} del miembro {MemberId} congelada {Start}..{End}; fin corrido {Days} días",
                moving.Id, moving.MemberId, freeze.StartsOn, freeze.EndsOn, daysAdded);
            await events.PublishAsync(
                new MembershipFrozen(moving.Id, moving.MemberId, gymId, freeze.StartsOn, freeze.EndsOn, daysAdded),
                cancellationToken);
        }

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

        // MS7 (§9): un grupo se reanuda junto. La solicitada tiene que estar congelada (si no, EndFreeze lo dice); las
        // demás se reanudan solo si lo están.
        var resumed = new List<(Membership Membership, FreezeEnd Ended)>();
        FreezeEnd? target = null;
        foreach (var moving in await MembershipLookup.GroupActiveAsync(memberships, membership, cancellationToken))
        {
            var own = moving.Id == membership.Id ? freezes : await memberships.GetFreezesAsync([moving.Id], cancellationToken);
            if (moving.Id != membership.Id && !own.Any(f => f.CoversDate(today)))
            {
                continue;
            }

            var originalEnd = moving.EndDate;
            var done = moving.EndFreeze(plan, own, today);
            if (done.Removed)
            {
                memberships.Remove(done.Freeze);
            }

            await FreezeSpans.ShiftFollowingAsync(memberships, moving.MemberId, originalEnd, done.Delta, cancellationToken, moving.IsPass);
            resumed.Add((moving, done));
            if (moving.Id == membership.Id)
            {
                target = done;
            }
        }

        await unitOfWork.SaveChangesAsync(cancellationToken);

        foreach (var (moving, done) in resumed)
        {
            logger.LogInformation(
                "Membresía {MembershipId} del miembro {MemberId} reanudada; fin corrido {Days} días", moving.Id, moving.MemberId, done.Delta);
            await events.PublishAsync(new MembershipResumed(moving.Id, moving.MemberId, gymId, done.Delta), cancellationToken);
        }

        var ended = target!;
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

        // MS7 (§9): el congelamiento de un grupo se deshace en todos. Cada integrante tiene su fila; se reconoce por tener
        // las mismas fechas que la solicitada.
        var removed = new List<(Membership Membership, int Delta)>();
        foreach (var moving in await MembershipLookup.GroupActiveAsync(memberships, membership, cancellationToken))
        {
            var target = moving.Id == membership.Id
                ? freeze
                : (await memberships.GetFreezesAsync([moving.Id], cancellationToken))
                    .FirstOrDefault(f => f.StartsOn == freeze.StartsOn && f.EndsOn == freeze.EndsOn);
            if (target is null)
            {
                continue;
            }

            var originalEnd = moving.EndDate;
            var delta = moving.RemoveScheduledFreeze(target, today);
            memberships.Remove(target);
            await FreezeSpans.ShiftFollowingAsync(memberships, moving.MemberId, originalEnd, delta, cancellationToken, moving.IsPass);
            removed.Add((moving, delta));
        }

        await unitOfWork.SaveChangesAsync(cancellationToken);

        foreach (var (moving, delta) in removed)
        {
            logger.LogInformation("Congelamiento {FreezeId} de la membresía {MembershipId} deshecho", freezeId, moving.Id);
            await events.PublishAsync(new MembershipFreezeRemoved(moving.Id, moving.MemberId, gymId, -delta), cancellationToken);
        }

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
