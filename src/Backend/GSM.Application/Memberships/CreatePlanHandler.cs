using GSM.Application.Common.Abstractions;
using GSM.Application.Memberships.Abstractions;
using GSM.Application.Memberships.Contracts;
using GSM.Domain.Common.Exceptions;
using GSM.Domain.Memberships;
using Microsoft.Extensions.Logging;

namespace GSM.Application.Memberships;

public sealed class CreatePlanHandler(
    ICurrentUserService currentUser,
    IMembershipRepository memberships,
    IUnitOfWork unitOfWork,
    ILogger<CreatePlanHandler> logger)
{
    public async Task<PlanResponse> HandleAsync(CreatePlanRequest request, CancellationToken cancellationToken)
    {
        var gymId = currentUser.RequireGymId();

        var name = request.Name.Trim();
        if (await memberships.PlanNameExistsAsync(name, excludePlanId: null, cancellationToken))
        {
            throw new ConflictException($"Ya existe un plan con el nombre '{name}'.");
        }

        var kind = PlanDuration.ParseKind(request.Kind);
        var plan = MembershipPlan.Create(gymId, name, request.Description, request.Price,
            PlanDuration.EffectiveDuration(kind, request.DurationValue, request.ValidityDays),
            PlanDuration.ParseUnit(request.DurationUnit), request.MaxFreezeDays ?? 0,
            request.MinFreezeDays ?? MembershipPlan.DefaultMinFreezeDays, kind, request.Entries, request.Seats ?? 1);
        memberships.Add(plan);
        await unitOfWork.SaveChangesAsync(cancellationToken);

        logger.LogInformation("Plan {PlanId} creado en gym {GymId}", plan.Id, gymId);
        return MembershipMapper.ToResponse(plan);
    }
}
