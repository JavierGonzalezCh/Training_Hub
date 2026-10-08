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
    IUnitOfWork unitOfWork,
    IDomainEventPublisher events,
    ILogger<AssignMembershipHandler> logger)
{
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

        var resolved = await renewals.ResolveAsync(
            gymId, memberId, request.StartDate, request.StartDateReason, cancellationToken);

        var membership = Membership.Create(
            gymId, memberId, plan.Id, resolved.StartDate, plan.DurationDays,
            resolved.Plan.Kind, resolved.StartOverrideReason);
        memberships.Add(membership);
        await unitOfWork.SaveChangesAsync(cancellationToken);

        logger.LogInformation(
            "Membresía {MembershipId} {Kind} para miembro {MemberId} ({Start}..{End})",
            membership.Id, resolved.Plan.Kind, memberId, membership.StartDate, membership.EndDate);

        await events.PublishAsync(
            resolved.PreviousMembershipId is { } previousId
                ? new MembershipRenewed(membership.Id, memberId, gymId, plan.Id, previousId)
                : new MembershipCreated(membership.Id, memberId, gymId, plan.Id),
            cancellationToken);

        return MembershipMapper.ToResponse(membership, plan.Name, today);
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
            gymId, memberId, startDate, startDateReason, cancellationToken, requireReason: false);

        return new RenewalPreviewResponse(
            GSM.Domain.Members.EnumText.Snake(resolved.Plan.Kind)!,
            resolved.Plan.StartDate,
            resolved.StartDate,
            resolved.StartDate.AddDays(plan.DurationDays - 1),
            resolved.Plan.PreviousEndDate,
            resolved.StartDate != resolved.Plan.StartDate);
    }
}
