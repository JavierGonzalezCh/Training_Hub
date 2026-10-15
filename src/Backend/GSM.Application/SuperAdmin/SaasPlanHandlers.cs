using GSM.Application.Common.Abstractions;
using GSM.Application.Memberships.Public;
using GSM.Application.SuperAdmin.Abstractions;
using GSM.Application.SuperAdmin.Contracts;
using GSM.Domain.Common.Exceptions;
using GSM.Domain.SuperAdmin;
using Microsoft.Extensions.Logging;

namespace GSM.Application.SuperAdmin;

internal static class SaasPlanMapper
{
    public static SaasPlanItem ToItem(SaasPlan p) => new(p.Id, p.Name, p.MemberLimit, p.SuggestedPrice, p.IsActive);
}

/// <summary>Planes del SaaS con su límite de miembros (SuperAdmin §V1).</summary>
public sealed class ListSaasPlansHandler(ISaasPlanRepository plans)
{
    public async Task<IReadOnlyList<SaasPlanItem>> HandleAsync(CancellationToken cancellationToken)
        => (await plans.ListAsync(cancellationToken)).Select(SaasPlanMapper.ToItem).ToList();
}

public sealed class CreateSaasPlanHandler(
    ISaasPlanRepository plans,
    IUnitOfWork unitOfWork,
    ILogger<CreateSaasPlanHandler> logger)
{
    public async Task<SaasPlanItem> HandleAsync(CreateSaasPlanRequest request, CancellationToken cancellationToken)
    {
        var plan = SaasPlan.Create(request.Name, request.MemberLimit, request.SuggestedPrice);

        if (await plans.NameExistsAsync(plan.Name, excludingId: null, cancellationToken))
        {
            throw new ConflictException($"Ya existe un plan llamado '{plan.Name}'.");
        }

        plans.Add(plan);
        await unitOfWork.SaveChangesAsync(cancellationToken);

        logger.LogInformation("Plan del SaaS {PlanId} '{Name}' creado (límite {Limit})", plan.Id, plan.Name, plan.MemberLimit);
        return SaasPlanMapper.ToItem(plan);
    }
}

/// <summary>
/// Edita un plan. Cambiar su límite aplica a todos los gyms que lo usan sin override; nadie queda
/// expulsado, solo se bloquean las membresías nuevas para quien hoy no cuenta.
/// </summary>
public sealed class UpdateSaasPlanHandler(
    ISaasPlanRepository plans,
    IUnitOfWork unitOfWork,
    ILogger<UpdateSaasPlanHandler> logger)
{
    public async Task<SaasPlanItem> HandleAsync(
        Guid planId, UpdateSaasPlanRequest request, CancellationToken cancellationToken)
    {
        var plan = await plans.GetByIdAsync(planId, cancellationToken)
            ?? throw new NotFoundException("El plan no existe.");

        plan.Update(request.Name, request.MemberLimit, request.SuggestedPrice, request.IsActive);

        if (await plans.NameExistsAsync(plan.Name, excludingId: plan.Id, cancellationToken))
        {
            throw new ConflictException($"Ya existe un plan llamado '{plan.Name}'.");
        }

        await unitOfWork.SaveChangesAsync(cancellationToken);

        logger.LogInformation("Plan del SaaS {PlanId} actualizado (límite {Limit}, activo {Active})",
            plan.Id, plan.MemberLimit, plan.IsActive);
        return SaasPlanMapper.ToItem(plan);
    }
}

/// <summary>
/// Uso del límite de miembros del gym del usuario ("142 / 150"). Lo lee el panel del gym; el límite sale
/// de SuperAdmin y el recuento de Memberships. Un gym sin suscripción no tiene límite.
/// </summary>
public sealed class GetMemberUsageHandler(
    ICurrentUserService currentUser,
    ISaasPlanRepository plans,
    IMembershipReader memberships)
{
    public async Task<MemberUsageResponse> HandleAsync(CancellationToken cancellationToken)
    {
        var gymId = currentUser.RequireGymId();
        var limit = await plans.GetGymLimitAsync(gymId, cancellationToken);
        var counted = await memberships.GetCountedMemberCountAsync(gymId, cancellationToken);
        return new MemberUsageResponse(counted, limit?.Effective, limit?.PlanName ?? string.Empty);
    }
}
