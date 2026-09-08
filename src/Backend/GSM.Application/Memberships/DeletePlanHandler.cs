using GSM.Application.Common.Abstractions;
using GSM.Application.Memberships.Abstractions;
using GSM.Domain.Common.Exceptions;
using Microsoft.Extensions.Logging;

namespace GSM.Application.Memberships;

/// <summary>Baja de un plan: soft delete a <c>is_active = false</c>. Las membresías existentes no se tocan.</summary>
public sealed class DeletePlanHandler(
    ICurrentUserService currentUser,
    IMembershipRepository memberships,
    IUnitOfWork unitOfWork,
    ILogger<DeletePlanHandler> logger)
{
    public async Task HandleAsync(Guid planId, CancellationToken cancellationToken)
    {
        _ = currentUser.RequireGymId();

        var plan = await memberships.GetPlanByIdAsync(planId, cancellationToken)
            ?? throw new NotFoundException("Plan no encontrado.");

        if (!plan.IsActive)
        {
            return;
        }

        plan.Deactivate();
        await unitOfWork.SaveChangesAsync(cancellationToken);
        logger.LogInformation("Plan {PlanId} desactivado", planId);
    }
}
