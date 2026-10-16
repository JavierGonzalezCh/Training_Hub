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
/// Asigna un plan a un miembro. El inicio lo calcula el sistema según su situación (§4.1 de
/// Memberships v2: <c>new</c>, <c>early</c>, <c>in_grace</c>, <c>after_grace</c>); el staff puede
/// cambiarlo con motivo. Nunca hay dos membresías cubriendo la misma fecha.
/// </summary>
public sealed class AssignMembershipHandler(
    ICurrentUserService currentUser,
    IClock clock,
    IMemberReader members,
    IMembershipRepository memberships,
    RenewalResolver renewals,
    IMemberLimitProvider memberLimits,
    IMembershipReader membershipReader,
    IUnitOfWork unitOfWork,
    IDomainEventPublisher events,
    ILogger<AssignMembershipHandler> logger)
{
    /// <summary>Texto al staff cuando el gym llega al límite de su plan (SuperAdmin §V1).</summary>
    public static string LimitMessage(int limit)
        => $"Llegaste al límite de {limit} miembros activos de tu plan. Habla con GSM para ampliarlo.";

    public async Task<MembershipResponse> HandleAsync(
        Guid memberId, AssignMembershipRequest request, CancellationToken cancellationToken)
    {
        var gymId = currentUser.RequireGymId();
        var today = clock.Today;

        var member = await members.GetAsync(memberId, cancellationToken)
            ?? throw new NotFoundException("Miembro no encontrado.");
        if (member.Status != MemberStatus.Active)
        {
            throw new ConflictException("No se puede asignar una membresía a un miembro inactivo.");
        }

        var plan = await memberships.GetPlanByIdAsync(request.PlanId, cancellationToken)
            ?? throw new NotFoundException("Plan no encontrado.");
        if (!plan.IsActive)
        {
            throw new ConflictException("El plan está inactivo.");
        }

        if (plan.IsGroup)
        {
            throw new ValidationDomainException(
                "Este plan es de grupo: asígnalo con «Asignar plan de grupo», eligiendo al titular y a los beneficiarios.");
        }

        await EnsureWithinMemberLimitAsync(gymId, memberId, cancellationToken);

        var resolved = await renewals.ResolveAsync(
            gymId, memberId, request.StartDate, request.StartDateReason, cancellationToken, newPlan: plan);

        // Precio acordado (descuento con motivo) y matrícula: las reglas viven en el dominio.
        var pricing = MembershipPricing.Resolve(
            plan.Price, request.AgreedPrice, request.DiscountReason,
            resolved.GymEnrollmentFee, resolved.IsFirstMembership,
            request.WaiveEnrollment, request.EnrollmentWaivedReason);

        var membership = Membership.Create(
            gymId, memberId, plan.Id, resolved.StartDate, plan.DurationValue,
            resolved.Plan.Kind, resolved.StartOverrideReason, pricing,
            plan.DurationUnit, resolved.InheritedCycleDay, plan.Entries);
        memberships.Add(membership);
        await unitOfWork.SaveChangesAsync(cancellationToken);

        logger.LogInformation(
            "Membresía {MembershipId} {Kind} para miembro {MemberId} ({Start}..{End}), total a cobrar {Total}",
            membership.Id, resolved.Plan.Kind, memberId, membership.StartDate, membership.EndDate, membership.TotalToCharge);

        await events.PublishAsync(
            resolved.PreviousMembershipId is { } previousId
                ? new MembershipRenewed(membership.Id, memberId, gymId, plan.Id, previousId)
                : new MembershipCreated(membership.Id, memberId, gymId, plan.Id),
            cancellationToken);

        return MembershipMapper.ToResponse(membership, plan.Name, today);
    }

    /// <summary>
    /// Al llegar al límite del plan del SaaS se bloquea dar membresía a quien hoy no cuenta (miembro
    /// nuevo o que vuelve). Renovar a quien ya cuenta nunca se bloquea: no se le niega la renovación
    /// a un miembro actual.
    /// </summary>
    private async Task EnsureWithinMemberLimitAsync(Guid gymId, Guid memberId, CancellationToken cancellationToken)
    {
        var limit = await memberLimits.GetLimitAsync(gymId, cancellationToken);
        if (limit is not { } max)
        {
            return;
        }

        var access = await membershipReader.GetAccessStatusAsync(memberId, cancellationToken);
        if (access.Commercial.CountsTowardMemberLimit())
        {
            return;
        }

        var counted = await membershipReader.GetCountedMemberCountAsync(gymId, cancellationToken);
        if (counted >= max)
        {
            logger.LogWarning(
                "Membresía bloqueada por el límite del plan en el gym {GymId}: {Counted}/{Limit}", gymId, counted, max);
            throw new ConflictException(LimitMessage(max));
        }
    }
}

/// <summary>
/// Vista previa de una asignación: cuándo empezaría y cuándo vencería, sin crear nada ("Empieza el
/// 8 oct · vence el 7 nov"). Usa el mismo cálculo que <see cref="AssignMembershipHandler"/>.
/// </summary>
public sealed class PreviewMembershipHandler(
    ICurrentUserService currentUser,
    IMemberReader members,
    IMembershipRepository memberships,
    RenewalResolver renewals)
{
    public async Task<RenewalPreviewResponse> HandleAsync(
        Guid memberId, Guid planId, DateOnly? startDate, string? startDateReason, CancellationToken cancellationToken)
    {
        var gymId = currentUser.RequireGymId();

        _ = await members.GetAsync(memberId, cancellationToken)
            ?? throw new NotFoundException("Miembro no encontrado.");

        var plan = await memberships.GetPlanByIdAsync(planId, cancellationToken)
            ?? throw new NotFoundException("Plan no encontrado.");

        var resolved = await renewals.ResolveAsync(
            gymId, memberId, startDate, startDateReason, cancellationToken, requireReason: false, newPlan: plan);

        return new RenewalPreviewResponse(
            GSM.Domain.Members.EnumText.Snake(resolved.Plan.Kind)!,
            resolved.Plan.StartDate,
            resolved.StartDate,
            MembershipCalendar.Resolve(
                resolved.StartDate, plan.DurationUnit, plan.DurationValue, resolved.InheritedCycleDay).EndDate,
            resolved.Plan.PreviousEndDate,
            resolved.StartDate != resolved.Plan.StartDate,
            resolved.IsFirstMembership ? resolved.GymEnrollmentFee : 0m,
            plan.Price);
    }
}
