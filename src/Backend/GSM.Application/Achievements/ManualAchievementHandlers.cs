using FluentValidation;
using GSM.Application.Achievements.Abstractions;
using GSM.Application.Achievements.Contracts;
using GSM.Application.Achievements.Events;
using GSM.Application.Common.Abstractions;
using GSM.Application.Common.Events;
using GSM.Application.Members.Public;
using GSM.Domain.Achievements;
using GSM.Domain.Common.Exceptions;
using Microsoft.Extensions.Logging;

namespace GSM.Application.Achievements;

public sealed class GrantManualAchievementRequestValidator : AbstractValidator<GrantManualAchievementRequest>
{
    public GrantManualAchievementRequestValidator()
    {
        RuleFor(x => x.MemberId).NotEmpty();
        RuleFor(x => x.Reason).NotEmpty().MaximumLength(300);
    }
}

public sealed class RevokeManualAchievementRequestValidator : AbstractValidator<RevokeManualAchievementRequest>
{
    public RevokeManualAchievementRequestValidator()
    {
        RuleFor(x => x.Reason).NotEmpty().MaximumLength(300);
    }
}

/// <summary>
/// El staff (Owner o cualquier staff) otorga un logro <c>manual</c> a un miembro, con motivo (L4, spec §3.4).
/// Una sola vez por miembro: repetirlo es 409. Si estaba retirado, se reabre la misma fila. Publica
/// <see cref="AchievementUnlockedEvent"/> después de guardar; Notifications crea el aviso (idempotente por fila).
/// </summary>
public sealed class GrantManualAchievementHandler(
    ICurrentUserService currentUser,
    IClock clock,
    IMemberReader members,
    IAchievementRepository achievements,
    IUnitOfWork unitOfWork,
    IDomainEventPublisher events,
    ILogger<GrantManualAchievementHandler> logger)
{
    public async Task<MemberAchievementItem> HandleAsync(
        Guid achievementId, GrantManualAchievementRequest request, CancellationToken cancellationToken)
    {
        var gymId = currentUser.RequireGymId();
        var staffId = currentUser.UserId ?? throw new ForbiddenDomainException("Falta la identidad de quien otorga.");

        var achievement = await achievements.GetByIdAsync(achievementId, cancellationToken)
            ?? throw new NotFoundException("Logro no encontrado.");
        if (!achievement.IsManual)
        {
            throw new ConflictException("Solo se otorgan a mano los logros de tipo manual.");
        }

        if (!achievement.IsActive)
        {
            throw new ConflictException("El logro está desactivado: actívalo antes de otorgarlo.");
        }

        // Tenant-filtrado: un miembro de otro gym no existe para este gym.
        var member = await members.GetAsync(request.MemberId, cancellationToken)
            ?? throw new NotFoundException("Miembro no encontrado.");

        var now = clock.UtcNow;
        var row = await achievements.GetUnlockAsync(member.Id, achievementId, cancellationToken);
        if (row is null)
        {
            row = MemberAchievement.GrantManual(gymId, member.Id, achievementId, staffId, request.Reason, now);
            achievements.AddUnlock(row);
        }
        else
        {
            row.RegrantManual(staffId, request.Reason, now);
        }

        await unitOfWork.SaveChangesAsync(cancellationToken);
        logger.LogInformation(
            "Logro manual {AchievementId} otorgado al miembro {MemberId} por {StaffId}", achievementId, member.Id, staffId);

        await events.PublishAsync(
            new AchievementUnlockedEvent(row.Id, member.Id, gymId, achievementId), cancellationToken);

        return new MemberAchievementItem(
            achievement.Id, achievement.Name, achievement.Description, achievement.IconUrl,
            achievement.ConditionType.ToString(), achievement.ConditionValue,
            true, row.UnlockedAt, 1, true, row.GrantReason);
    }
}

/// <summary>Retira un logro manual otorgado por error, con motivo (L4). Los automáticos nunca se retiran.</summary>
public sealed class RevokeManualAchievementHandler(
    ICurrentUserService currentUser,
    IClock clock,
    IMemberReader members,
    IAchievementRepository achievements,
    IUnitOfWork unitOfWork,
    ILogger<RevokeManualAchievementHandler> logger)
{
    public async Task HandleAsync(
        Guid achievementId, Guid memberId, RevokeManualAchievementRequest request, CancellationToken cancellationToken)
    {
        _ = currentUser.RequireGymId();
        var staffId = currentUser.UserId ?? throw new ForbiddenDomainException("Falta la identidad de quien retira.");

        _ = await achievements.GetByIdAsync(achievementId, cancellationToken)
            ?? throw new NotFoundException("Logro no encontrado.");
        _ = await members.GetAsync(memberId, cancellationToken)
            ?? throw new NotFoundException("Miembro no encontrado.");

        var row = await achievements.GetUnlockAsync(memberId, achievementId, cancellationToken)
            ?? throw new NotFoundException("El miembro no tiene este logro.");

        row.Revoke(staffId, request.Reason, clock.UtcNow);
        await unitOfWork.SaveChangesAsync(cancellationToken);
        logger.LogInformation(
            "Logro manual {AchievementId} retirado al miembro {MemberId} por {StaffId}", achievementId, memberId, staffId);
    }
}
