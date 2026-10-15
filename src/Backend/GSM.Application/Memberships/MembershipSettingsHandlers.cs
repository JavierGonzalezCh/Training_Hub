using GSM.Application.Common.Abstractions;
using GSM.Application.Members.Public;
using GSM.Application.Memberships.Abstractions;
using GSM.Application.Memberships.Contracts;
using GSM.Application.Memberships.Public;
using GSM.Domain.Common.Exceptions;
using GSM.Domain.Memberships;
using Microsoft.Extensions.Logging;

namespace GSM.Application.Memberships;

public sealed class GetMembershipSettingsHandler(
    ICurrentUserService currentUser, IMembershipRepository memberships)
{
    public async Task<MembershipSettingsResponse> HandleAsync(CancellationToken cancellationToken)
    {
        var gymId = currentUser.RequireGymId();
        var current = await memberships.GetSettingsAsync(gymId, cancellationToken)
            ?? MembershipSettings.Default(gymId);
        return new MembershipSettingsResponse(current.GraceDays, current.ExpiringDays, current.EnrollmentFee);
    }
}

/// <summary>Gracia, ventana de "por vencer" del gym. Sin fila valen los valores por defecto; la primera edición la crea.</summary>
public sealed class UpdateMembershipSettingsHandler(
    ICurrentUserService currentUser,
    IMembershipRepository memberships,
    IUnitOfWork unitOfWork,
    ILogger<UpdateMembershipSettingsHandler> logger)
{
    public async Task<MembershipSettingsResponse> HandleAsync(
        UpdateMembershipSettingsRequest request, CancellationToken cancellationToken)
    {
        var gymId = currentUser.RequireGymId();
        var current = await memberships.GetSettingsAsync(gymId, cancellationToken);
        if (current is null)
        {
            current = MembershipSettings.Default(gymId);
            memberships.Add(current);
        }

        current.Update(request.GraceDays, request.ExpiringDays, request.EnrollmentFee);
        await unitOfWork.SaveChangesAsync(cancellationToken);

        logger.LogInformation(
            "Ajustes de membresías del gym {GymId}: gracia {GraceDays} días, aviso {ExpiringDays} días, matrícula {EnrollmentFee}",
            gymId, current.GraceDays, current.ExpiringDays, current.EnrollmentFee);
        return new MembershipSettingsResponse(current.GraceDays, current.ExpiringDays, current.EnrollmentFee);
    }
}

/// <summary>Estado comercial y veredicto de acceso de un miembro hoy (tarjeta de recepción y ficha).</summary>
public sealed class GetAccessStatusHandler(
    ICurrentUserService currentUser, IMemberReader members, IMembershipReader memberships)
{
    public async Task<AccessStatusResponse> HandleAsync(Guid memberId, CancellationToken cancellationToken)
    {
        _ = currentUser.RequireGymId();

        _ = await members.GetAsync(memberId, cancellationToken)
            ?? throw new NotFoundException("Miembro no encontrado.");

        return MembershipMapper.ToResponse(await memberships.GetAccessStatusAsync(memberId, cancellationToken));
    }
}

/// <summary>Vuelve a ofrecer un plan desactivado.</summary>
public sealed class ReactivatePlanHandler(
    ICurrentUserService currentUser,
    IMembershipRepository memberships,
    IUnitOfWork unitOfWork,
    ILogger<ReactivatePlanHandler> logger)
{
    public async Task<PlanResponse> HandleAsync(Guid planId, CancellationToken cancellationToken)
    {
        _ = currentUser.RequireGymId();

        var plan = await memberships.GetPlanByIdAsync(planId, cancellationToken)
            ?? throw new NotFoundException("Plan no encontrado.");

        if (plan.IsActive)
        {
            throw new ConflictException("El plan ya está activo.");
        }

        plan.Activate();
        await unitOfWork.SaveChangesAsync(cancellationToken);

        logger.LogInformation("Plan {PlanId} reactivado", planId);
        return MembershipMapper.ToResponse(plan);
    }
}
