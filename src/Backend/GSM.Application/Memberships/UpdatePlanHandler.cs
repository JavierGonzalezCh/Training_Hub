using GSM.Application.Common.Abstractions;
using GSM.Application.Memberships.Abstractions;
using GSM.Application.Memberships.Contracts;
using GSM.Domain.Common.Exceptions;

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

        plan.Update(name, request.Description, request.Price, request.DurationValue,
            PlanDuration.ParseUnit(request.DurationUnit));
        await unitOfWork.SaveChangesAsync(cancellationToken);

        return MembershipMapper.ToResponse(plan);
    }
}
