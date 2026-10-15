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

        var plan = MembershipPlan.Create(gymId, name, request.Description, request.Price, request.DurationValue,
            PlanDuration.ParseUnit(request.DurationUnit), request.MaxFreezeDays ?? 0, request.MinFreezeDays ?? MembershipPlan.DefaultMinFreezeDays);
        memberships.Add(plan);
        await unitOfWork.SaveChangesAsync(cancellationToken);

        logger.LogInformation("Plan {PlanId} creado en gym {GymId}", plan.Id, gymId);
        return MembershipMapper.ToResponse(plan);
    }
}
