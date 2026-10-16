using GSM.Application.Common.Abstractions;
using GSM.Application.Common.Events;
using GSM.Application.Members.Public;
using GSM.Application.Memberships.Abstractions;
using GSM.Application.Memberships.Contracts;
using GSM.Application.Memberships.Events;
using GSM.Application.Memberships.Public;
using GSM.Domain.Common.Exceptions;
using GSM.Domain.Members;
using GSM.Domain.Memberships;
using Microsoft.Extensions.Logging;

namespace GSM.Application.Memberships;

/// <summary>
/// Guarda del límite de miembros del plan del SaaS (SuperAdmin §V1) para una asignación que mete a varias personas
/// de una vez (un grupo). Cada persona que hoy no cuenta suma uno; si el total no cabe, se bloquea toda la operación.
/// Renovar a quien ya cuenta nunca se bloquea.
/// </summary>
public sealed class MemberLimitGuard(
    IMemberLimitProvider memberLimits,
    IMembershipReader membershipReader,
    ILogger<MemberLimitGuard> logger)
{
    public async Task EnsureAsync(Guid gymId, IReadOnlyCollection<Guid> memberIds, CancellationToken cancellationToken)
    {
        var limit = await memberLimits.GetLimitAsync(gymId, cancellationToken);
        if (limit is not { } max)
        {
            return;
        }

        var newlyCounted = 0;
        foreach (var memberId in memberIds)
        {
            var access = await membershipReader.GetAccessStatusAsync(memberId, cancellationToken);
            if (!access.Commercial.CountsTowardMemberLimit())
            {
                newlyCounted++;
            }
        }

        if (newlyCounted == 0)
        {
            return;
        }

        var counted = await membershipReader.GetCountedMemberCountAsync(gymId, cancellationToken);
        if (counted + newlyCounted > max)
        {
            logger.LogWarning(
                "Grupo bloqueado por el límite del plan en el gym {GymId}: {Counted} + {New} > {Limit}",
                gymId, counted, newlyCounted, max);
            throw new ConflictException(AssignMembershipHandler.LimitMessage(max));
        }
    }
}

/// <summary>Lo que crea asignar o renovar un grupo, antes de guardar.</summary>
public sealed record GroupCreation(
    MembershipGroup Group, Membership Holder, IReadOnlyList<Membership> Beneficiaries, ResolvedRenewal Resolved);

/// <summary>
/// Planes de grupo (MS7, Memberships v2 §9). Lo comparten asignar, renovar y meter un beneficiario: validan y
/// construyen las membresías (una por persona, mismas fechas y ciclo, el precio en la del titular) y arman la
/// respuesta del grupo. No guarda: lo hace quien llama, en su transacción.
/// </summary>
public sealed class GroupMembershipService(
    IClock clock,
    IMemberReader members,
    IMembershipRepository memberships,
    RenewalResolver renewals,
    MemberLimitGuard limitGuard,
    IDomainEventPublisher events)
{
    /// <summary>
    /// Crea el grupo, la membresía del titular (con la regla de renovación de §4.1, calculada sobre su historial) y las
    /// de los beneficiarios con las mismas fechas. El titular paga: precio acordado, descuento y matrícula van en su
    /// membresía; las de los beneficiarios valen 0 y no cobran matrícula.
    /// </summary>
    public async Task<GroupCreation> CreateAsync(
        Guid gymId,
        Guid holderId,
        MembershipPlan plan,
        IReadOnlyList<Guid> beneficiaryIds,
        DateOnly? startDate,
        string? startDateReason,
        decimal? agreedPrice,
        string? discountReason,
        bool waiveEnrollment,
        string? enrollmentWaivedReason,
        CancellationToken cancellationToken)
    {
        if (!plan.IsGroup)
        {
            throw new ValidationDomainException("Este plan no es de grupo: asígnalo como una membresía individual.");
        }

        if (!plan.IsActive)
        {
            throw new ConflictException("El plan está inactivo.");
        }

        var ids = beneficiaryIds.ToList();
        if (ids.Distinct().Count() != ids.Count)
        {
            throw new ValidationDomainException("Un beneficiario aparece más de una vez.");
        }

        if (ids.Contains(holderId))
        {
            throw new ValidationDomainException("El titular no puede ser también beneficiario.");
        }

        MembershipGroup.RequireBeneficiaryCount(plan.Seats, ids.Count);

        await RequireActiveMemberAsync(holderId, cancellationToken);
        foreach (var id in ids)
        {
            await RequireActiveMemberAsync(id, cancellationToken);
        }

        await limitGuard.EnsureAsync(gymId, [holderId, .. ids], cancellationToken);

        var resolved = await renewals.ResolveAsync(
            gymId, holderId, startDate, startDateReason, cancellationToken, newPlan: plan);
        var pricing = MembershipPricing.Resolve(
            plan.Price, agreedPrice, discountReason,
            resolved.GymEnrollmentFee, resolved.IsFirstMembership, waiveEnrollment, enrollmentWaivedReason);

        var group = MembershipGroup.Create(gymId, plan.Id, holderId, plan.Seats);
        var holder = Membership.Create(
            gymId, holderId, plan.Id, resolved.StartDate, plan.DurationValue,
            resolved.Plan.Kind, resolved.StartOverrideReason, pricing,
            plan.DurationUnit, resolved.InheritedCycleDay, groupId: group.Id, isGroupHolder: true);

        var beneficiaries = new List<Membership>();
        foreach (var id in ids)
        {
            await RequireNoOverlapAsync(id, holder.StartDate, holder.EndDate, cancellationToken);
            beneficiaries.Add(Membership.CreateGroupBeneficiary(
                gymId, id, plan.Id, group.Id, holder.StartDate, holder.EndDate, holder.CycleDay, resolved.Plan.Kind));
        }

        memberships.Add(group);
        memberships.Add(holder);
        foreach (var beneficiary in beneficiaries)
        {
            memberships.Add(beneficiary);
        }

        return new GroupCreation(group, holder, beneficiaries, resolved);
    }

    /// <summary>
    /// Publica los eventos de lo recién creado, uno por persona, como si se hubieran asignado una a una: los
    /// manejadores existentes (logros, avisos) no distinguen grupos. <paramref name="previousByMember"/> trae la
    /// membresía anterior de quien se renueva; el resto es una membresía nueva.
    /// </summary>
    public async Task PublishCreatedAsync(
        GroupCreation creation, Guid gymId, IReadOnlyDictionary<Guid, Guid> previousByMember,
        CancellationToken cancellationToken)
    {
        foreach (var membership in new[] { creation.Holder }.Concat(creation.Beneficiaries))
        {
            await events.PublishAsync(
                previousByMember.TryGetValue(membership.MemberId, out var previousId)
                    ? new MembershipRenewed(membership.Id, membership.MemberId, gymId, membership.PlanId, previousId)
                    : new MembershipCreated(membership.Id, membership.MemberId, gymId, membership.PlanId),
                cancellationToken);
        }
    }

    /// <summary>Respuesta del grupo: sus membresías (con congelamientos), nombres y cupos. Lee de la base.</summary>
    public async Task<MembershipGroupResponse> BuildResponseAsync(
        MembershipGroup group, CancellationToken cancellationToken)
    {
        var today = clock.Today;
        var all = await memberships.GetByGroupAsync(group.Id, cancellationToken);
        var holder = all.FirstOrDefault(m => m.IsGroupHolder)
            ?? throw new NotFoundException("Grupo sin titular.");
        var freezes = await memberships.GetFreezesAsync(all.Select(m => m.Id).ToList(), cancellationToken);
        var names = await members.GetFullNamesAsync(all.Select(m => m.MemberId).Distinct().ToList(), cancellationToken);
        var planName = (await memberships.GetPlanByIdAsync(group.PlanId, cancellationToken))?.Name ?? string.Empty;

        var rows = all
            .Select(m => new MembershipGroupMemberResponse(
                m.MemberId,
                names.GetValueOrDefault(m.MemberId, string.Empty),
                m.IsGroupHolder,
                MembershipMapper.ToResponse(m, planName, today, freezes)))
            .ToList();
        var used = all.Count(m => m.Status == MembershipStatus.Active);

        return new MembershipGroupResponse(
            group.Id, group.PlanId, planName, group.Seats, used, Math.Max(0, group.Seats - used),
            group.HolderMemberId, holder.Id, holder.StartDate, holder.EndDate, holder.TotalToCharge, rows);
    }

    private async Task RequireActiveMemberAsync(Guid memberId, CancellationToken cancellationToken)
    {
        var member = await members.GetAsync(memberId, cancellationToken)
            ?? throw new NotFoundException("Miembro no encontrado.");
        if (member.Status != MemberStatus.Active)
        {
            throw new ConflictException($"{member.FirstName} {member.LastName} está dado de baja y no puede entrar a un grupo.");
        }
    }

    /// <summary>Nunca hay dos membresías por tiempo cubriendo el mismo día: quien ya está cubierto en esas fechas no entra.</summary>
    public async Task RequireNoOverlapAsync(
        Guid memberId, DateOnly start, DateOnly end, CancellationToken cancellationToken)
    {
        var overlapping = await memberships.GetActiveOverlappingAsync(memberId, start, end, cancellationToken);
        if (overlapping.Count == 0)
        {
            return;
        }

        var member = await members.GetAsync(memberId, cancellationToken);
        var name = member is null ? "El miembro" : $"{member.FirstName} {member.LastName}";
        throw new ConflictException(
            $"{name} ya tiene una membresía vigente entre {overlapping[0].StartDate:yyyy-MM-dd} y {overlapping[^1].EndDate:yyyy-MM-dd} " +
            "que se cruza con las fechas del grupo. Cancélala o espera a que venza.");
    }
}
