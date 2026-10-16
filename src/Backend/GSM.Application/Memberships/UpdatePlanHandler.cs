using GSM.Application.Common.Abstractions;
using GSM.Application.Memberships.Abstractions;
using GSM.Application.Memberships.Contracts;
using GSM.Domain.Common.Exceptions;
using GSM.Domain.Memberships;

namespace GSM.Application.Memberships;

public sealed class UpdatePlanHandler(
    ICurrentUserService currentUser,
    IMembershipRepository memberships,
    IUnitOfWork unitOfWork)
{
    public async Task<PlanResponse> HandleAsync(
        Guid planId, UpdatePlanRequest request, CancellationToken cancellationToken)
    {
        _ = currentUser.RequireGymId();

        var plan = await memberships.GetPlanByIdAsync(planId, cancellationToken)
            ?? throw new NotFoundException("Plan no encontrado.");

        var name = request.Name.Trim();
        if (await memberships.PlanNameExistsAsync(name, excludePlanId: planId, cancellationToken))
        {
            throw new ConflictException($"Ya existe otro plan con el nombre '{name}'.");
        }

        // El tipo no se cambia; si llega, el dominio lo rechaza cuando difiere del actual.
        var requestedKind = string.IsNullOrWhiteSpace(request.Kind) ? (PlanKind?)null : PlanDuration.ParseKind(request.Kind);
        if (requestedKind is { } asked && asked != plan.Kind)
        {
            throw new ValidationDomainException("El tipo de un plan no se puede cambiar. Crea otro plan o desactiva este.");
        }

        plan.Update(name, request.Description, request.Price,
            PlanDuration.EffectiveDuration(plan.Kind, request.DurationValue, request.ValidityDays ?? plan.ValidityDays),
            PlanDuration.ParseUnit(request.DurationUnit), request.MaxFreezeDays, request.MinFreezeDays,
            requestedKind, request.Entries, request.Seats);
        await unitOfWork.SaveChangesAsync(cancellationToken);

        return MembershipMapper.ToResponse(plan);
    }
}
