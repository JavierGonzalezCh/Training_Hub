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
/// Asigna un plan a un miembro. Si ya tiene una membresía Active vigente (o futura), la nueva
/// se encadena al día siguiente de su <c>end_date</c>, de modo que nunca hay dos cubriendo la
/// misma fecha. Si no, arranca en la fecha pedida (o hoy).
/// </summary>
public sealed class AssignMembershipHandler(
    ICurrentUserService currentUser,
    IClock clock,
    IMemberReader members,
    IMembershipRepository memberships,
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

        var current = await memberships.GetLatestOpenAsync(memberId, today, cancellationToken);

        var startDate = current is not null
            ? current.EndDate.AddDays(1)
            : request.StartDate ?? today;

        var membership = Membership.Create(gymId, memberId, plan.Id, startDate, plan.DurationDays);
        memberships.Add(membership);
        await unitOfWork.SaveChangesAsync(cancellationToken);

        logger.LogInformation(
            "Membresía {MembershipId} {Kind} para miembro {MemberId} ({Start}..{End})",
            membership.Id, current is null ? "creada" : "renovada", memberId,
            membership.StartDate, membership.EndDate);

        await events.PublishAsync(
            current is null
                ? new MembershipCreated(membership.Id, memberId, gymId, plan.Id)
                : new MembershipRenewed(membership.Id, memberId, gymId, plan.Id, current.Id),
            cancellationToken);

        return MembershipMapper.ToResponse(membership, plan.Name, today);
    }
}
