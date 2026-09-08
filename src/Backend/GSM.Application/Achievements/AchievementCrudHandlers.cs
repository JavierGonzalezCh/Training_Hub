using GSM.Application.Achievements.Abstractions;
using GSM.Application.Achievements.Contracts;
using GSM.Application.Common.Abstractions;
using GSM.Domain.Achievements;
using GSM.Domain.Common.Exceptions;

namespace GSM.Application.Achievements;

public sealed class CreateAchievementHandler(
    ICurrentUserService currentUser, IAchievementRepository achievements, IUnitOfWork unitOfWork)
{
    public async Task<AchievementResponse> HandleAsync(
        CreateAchievementRequest request, CancellationToken cancellationToken)
    {
        var gymId = currentUser.RequireGymId();
        var name = request.Name.Trim();

        if (await achievements.NameExistsAsync(name, null, cancellationToken))
        {
            throw new ConflictException($"Ya existe un logro con el nombre '{name}'.");
        }

        var achievement = Achievement.Create(
            gymId, name, request.Description, request.IconUrl, request.ConditionType, request.ConditionValue);
        achievements.Add(achievement);
        await unitOfWork.SaveChangesAsync(cancellationToken);

        return AchievementMapper.ToResponse(achievement);
    }
}

public sealed class UpdateAchievementHandler(
    ICurrentUserService currentUser, IAchievementRepository achievements, IUnitOfWork unitOfWork)
{
    public async Task<AchievementResponse> HandleAsync(
        Guid achievementId, UpdateAchievementRequest request, CancellationToken cancellationToken)
    {
        _ = currentUser.RequireGymId();

        var achievement = await achievements.GetByIdAsync(achievementId, cancellationToken)
            ?? throw new NotFoundException("Logro no encontrado.");

        var name = request.Name.Trim();
        if (await achievements.NameExistsAsync(name, achievementId, cancellationToken))
        {
            throw new ConflictException($"Ya existe otro logro con el nombre '{name}'.");
        }

        achievement.Update(
            name, request.Description, request.IconUrl, request.ConditionType, request.ConditionValue);
        await unitOfWork.SaveChangesAsync(cancellationToken);

        return AchievementMapper.ToResponse(achievement);
    }
}

public sealed class DeleteAchievementHandler(
    ICurrentUserService currentUser, IAchievementRepository achievements, IUnitOfWork unitOfWork)
{
    public async Task HandleAsync(Guid achievementId, CancellationToken cancellationToken)
    {
        _ = currentUser.RequireGymId();

        var achievement = await achievements.GetByIdAsync(achievementId, cancellationToken)
            ?? throw new NotFoundException("Logro no encontrado.");

        if (!achievement.IsActive)
        {
            return;
        }

        achievement.Deactivate();
        await unitOfWork.SaveChangesAsync(cancellationToken);
    }
}

public sealed class ListAchievementsHandler(
    ICurrentUserService currentUser, IAchievementRepository achievements)
{
    public async Task<IReadOnlyList<AchievementResponse>> HandleAsync(
        bool includeInactive, CancellationToken cancellationToken)
    {
        _ = currentUser.RequireGymId();
        var list = await achievements.ListAsync(includeInactive, cancellationToken);
        return list.Select(AchievementMapper.ToResponse).ToList();
    }
}
