using GSM.Application.Common.Abstractions;
using GSM.Application.Common.Events;
using GSM.Application.Members.Public;
using GSM.Application.Memberships.Abstractions;
using GSM.Application.Memberships.Contracts;
using GSM.Application.Memberships.Events;
using GSM.Domain.Common.Exceptions;
using GSM.Domain.Members;
using GSM.Domain.Memberships;
using Microsoft.Extensions.Logging;

namespace GSM.Application.Memberships;

/// <summary>
/// Asigna un plan de grupo (MS7, §9): una membresía por persona con el mismo <c>GroupId</c>, las mismas fechas y el
/// mismo ciclo. El titular paga (su membresía lleva el precio acordado y la matrícula); las de los beneficiarios valen 0.
/// Cobrar al titular <c>TotalToCharge</c> lo hace la UI con una segunda llamada a Payments, como al asignar una individual.
/// </summary>
public sealed class AssignGroupMembershipHandler(
    ICurrentUserService currentUser,
    IMembershipRepository memberships,
    GroupMembershipService groups,
    IUnitOfWork unitOfWork,
    ILogger<AssignGroupMembershipHandler> logger)
{
    public async Task<MembershipGroupResponse> HandleAsync(
        AssignGroupMembershipRequest request, CancellationToken cancellationToken)
    {
        var gymId = currentUser.RequireGymId();

        var plan = await memberships.GetPlanByIdAsync(request.PlanId, cancellationToken)
            ?? throw new NotFoundException("Plan no encontrado.");

        var creation = await groups.CreateAsync(
            gymId, request.HolderMemberId, plan, request.BeneficiaryMemberIds,
            request.StartDate, request.StartDateReason, request.AgreedPrice, request.DiscountReason,
            request.WaiveEnrollment, request.EnrollmentWaivedReason, cancellationToken);
        await unitOfWork.SaveChangesAsync(cancellationToken);

        logger.LogInformation(
            "Grupo {GroupId} ({Plan}) creado para el titular {HolderId} con {Beneficiaries} beneficiarios ({Start}..{End}), total a cobrar {Total}",
            creation.Group.Id, plan.Id, request.HolderMemberId, creation.Beneficiaries.Count,
            creation.Holder.StartDate, creation.Holder.EndDate, creation.Holder.TotalToCharge);

        var previous = new Dictionary<Guid, Guid>();
        if (creation.Resolved.PreviousMembershipId is { } previousId)
        {
            previous[creation.Holder.MemberId] = previousId;
        }

        await groups.PublishCreatedAsync(creation, gymId, previous, cancellationToken);

        return await groups.BuildResponseAsync(creation.Group, cancellationToken);
    }
}

/// <summary>
/// Renueva un grupo (§9: "se renuevan juntas"): crea otro grupo con el mismo titular que sigue al anterior según la
/// regla de §4.1. Por defecto conserva a los beneficiarios que siguen en el grupo; <c>BeneficiaryMemberIds</c> permite
/// cambiarlos. Cada persona queda con su membresía nueva y el titular paga el total.
/// </summary>
public sealed class RenewGroupMembershipHandler(
    ICurrentUserService currentUser,
    IMembershipRepository memberships,
    GroupMembershipService groups,
    IUnitOfWork unitOfWork,
    ILogger<RenewGroupMembershipHandler> logger)
{
    public async Task<MembershipGroupResponse> HandleAsync(
        Guid groupId, RenewGroupMembershipRequest request, CancellationToken cancellationToken)
    {
        var gymId = currentUser.RequireGymId();

        var previousGroup = await memberships.GetGroupByIdAsync(groupId, cancellationToken)
            ?? throw new NotFoundException("Grupo no encontrado.");
        var previousMemberships = await memberships.GetByGroupAsync(groupId, cancellationToken);

        var plan = await memberships.GetPlanByIdAsync(request.PlanId ?? previousGroup.PlanId, cancellationToken)
            ?? throw new NotFoundException("Plan no encontrado.");

        var currentBeneficiaries = previousMemberships
            .Where(m => !m.IsGroupHolder && m.Status == MembershipStatus.Active)
            .Select(m => m.MemberId)
            .ToList();
        var beneficiaryIds = request.BeneficiaryMemberIds?.ToList() ?? currentBeneficiaries;

        var creation = await groups.CreateAsync(
            gymId, previousGroup.HolderMemberId, plan, beneficiaryIds,
            request.StartDate, request.StartDateReason, request.AgreedPrice, request.DiscountReason,
            waiveEnrollment: false, enrollmentWaivedReason: null, cancellationToken);
        await unitOfWork.SaveChangesAsync(cancellationToken);

        logger.LogInformation(
            "Grupo {GroupId} renovado como {NewGroupId} ({Start}..{End}), total a cobrar {Total}",
            groupId, creation.Group.Id, creation.Holder.StartDate, creation.Holder.EndDate, creation.Holder.TotalToCharge);

        // Quien tenía membresía vigente en el grupo anterior se "renueva"; si la regla de §4.1 dice que el titular
        // empieza de cero (pasó la gracia), nadie es renovación.
        var previous = new Dictionary<Guid, Guid>();
        if (creation.Resolved.PreviousMembershipId is not null)
        {
            foreach (var old in previousMemberships.Where(m => m.Status == MembershipStatus.Active))
            {
                previous[old.MemberId] = old.Id;
            }
        }

        await groups.PublishCreatedAsync(creation, gymId, previous, cancellationToken);
        return await groups.BuildResponseAsync(creation.Group, cancellationToken);
    }
}

/// <summary>Un grupo con sus membresías, cupos y lo que se cobraría al titular. Solo lectura.</summary>
public sealed class GetMembershipGroupHandler(
    ICurrentUserService currentUser,
    IMembershipRepository memberships,
    GroupMembershipService groups)
{
    public async Task<MembershipGroupResponse> HandleAsync(Guid groupId, CancellationToken cancellationToken)
    {
        _ = currentUser.RequireGymId();
        var group = await memberships.GetGroupByIdAsync(groupId, cancellationToken)
            ?? throw new NotFoundException("Grupo no encontrado.");
        return await groups.BuildResponseAsync(group, cancellationToken);
    }
}

/// <summary>
/// Mete a otro miembro al grupo mientras haya cupo (§9). Hereda las fechas actuales del grupo (desde hoy, o desde el
/// inicio del grupo si aún no empieza) y no paga. Sacar a alguien es cancelar su membresía.
/// </summary>
public sealed class AddGroupBeneficiaryHandler(
    ICurrentUserService currentUser,
    IClock clock,
    IMemberReader members,
    IMembershipRepository memberships,
    GroupMembershipService groups,
    MemberLimitGuard limitGuard,
    IUnitOfWork unitOfWork,
    IDomainEventPublisher events,
    ILogger<AddGroupBeneficiaryHandler> logger)
{
    public async Task<MembershipGroupResponse> HandleAsync(
        Guid groupId, AddGroupBeneficiaryRequest request, CancellationToken cancellationToken)
    {
        var gymId = currentUser.RequireGymId();
        var today = clock.Today;

        var group = await memberships.GetGroupByIdAsync(groupId, cancellationToken)
            ?? throw new NotFoundException("Grupo no encontrado.");
        var all = await memberships.GetByGroupAsync(groupId, cancellationToken);
        var holder = all.FirstOrDefault(m => m.IsGroupHolder)
            ?? throw new NotFoundException("Grupo sin titular.");
        var active = all.Where(m => m.Status == MembershipStatus.Active).ToList();

        if (holder.Status != MembershipStatus.Active)
        {
            throw new ConflictException("El grupo está cancelado: asigna un grupo nuevo.");
        }

        if (holder.EndDate < today)
        {
            throw new ConflictException("El grupo ya venció: renuévalo para incluir a más personas.");
        }

        if (active.Any(m => m.MemberId == request.MemberId))
        {
            throw new ConflictException("Ese miembro ya está en el grupo.");
        }

        if (active.Count >= group.Seats)
        {
            throw new ConflictException($"El grupo no tiene cupos libres ({group.Seats} de {group.Seats} ocupados).");
        }

        // Una persona nueva con el grupo congelado quedaría con fechas distintas al resto: se reanuda primero.
        var freezes = await memberships.GetFreezesAsync(active.Select(m => m.Id).ToList(), cancellationToken);
        if (freezes.Any(f => f.EndsOn is null || f.EndsOn >= today))
        {
            throw new ConflictException(
                "El grupo tiene un congelamiento en curso o programado: termínalo antes de agregar a alguien.");
        }

        var member = await members.GetAsync(request.MemberId, cancellationToken)
            ?? throw new NotFoundException("Miembro no encontrado.");
        if (member.Status != MemberStatus.Active)
        {
            throw new ConflictException("No se puede agregar a un grupo a un miembro dado de baja.");
        }

        await limitGuard.EnsureAsync(gymId, [request.MemberId], cancellationToken);

        var start = holder.StartDate > today ? holder.StartDate : today;
        await groups.RequireNoOverlapAsync(request.MemberId, start, holder.EndDate, cancellationToken);

        var beneficiary = Membership.CreateGroupBeneficiary(
            gymId, request.MemberId, group.PlanId, group.Id, start, holder.EndDate, holder.CycleDay, RenewalKind.New);
        memberships.Add(beneficiary);
        await unitOfWork.SaveChangesAsync(cancellationToken);

        logger.LogInformation(
            "Miembro {MemberId} agregado al grupo {GroupId} ({Start}..{End})", request.MemberId, group.Id, start, holder.EndDate);
        await events.PublishAsync(
            new MembershipCreated(beneficiary.Id, request.MemberId, gymId, group.PlanId), cancellationToken);

        return await groups.BuildResponseAsync(group, cancellationToken);
    }
}
