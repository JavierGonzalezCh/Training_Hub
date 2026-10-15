using GSM.Application.Achievements.Abstractions;
using GSM.Application.Achievements.Contracts;
using GSM.Application.Common.Abstractions;
using GSM.Domain.Common.Exceptions;

namespace GSM.Application.Achievements;

/// <summary>Cuántos miembros recibirían el logro ahora (aviso previo a confirmar, spec §6). No escribe.</summary>
public sealed class PreviewRetroactiveAchievementHandler(
    ICurrentUserService currentUser, IRetroactiveAchievementGrant grant)
{
    public async Task<RetroactivePreviewResponse> HandleAsync(
        RetroactivePreviewRequest request, CancellationToken cancellationToken)
    {
        _ = currentUser.RequireGymId();
        if (!Enum.IsDefined(request.ConditionType))
        {
            throw new ValidationDomainException("La condición del logro no es válida.");
        }

        var count = await grant.CountEligibleAsync(
            request.ConditionType, request.ConditionValue, request.AchievementId, cancellationToken);
        return new RetroactivePreviewResponse(count);
    }
}

/// <summary>
/// Otorga el logro ya guardado a quien lo cumple (spec §6). Se llama tras crear el logro o bajar su valor.
/// Idempotente. No aplica a logros inactivos.
/// </summary>
public sealed class GrantRetroactiveAchievementHandler(
    ICurrentUserService currentUser, IAchievementRepository achievements, IRetroactiveAchievementGrant grant)
{
    public async Task<RetroactiveGrantResponse> HandleAsync(Guid achievementId, CancellationToken cancellationToken)
    {
        _ = currentUser.RequireGymId();

        var achievement = await achievements.GetByIdAsync(achievementId, cancellationToken)
            ?? throw new NotFoundException("Logro no encontrado.");

        if (!achievement.IsActive)
        {
            throw new ConflictException("El logro está desactivado: actívalo antes de otorgarlo.");
        }

        return new RetroactiveGrantResponse(await grant.GrantAsync(achievement, cancellationToken));
    }
}
