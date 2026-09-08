using GSM.Application.Achievements.Abstractions;
using GSM.Application.Achievements.Contracts;
using GSM.Application.Common;
using GSM.Application.Common.Abstractions;
using GSM.Domain.Common.Exceptions;

namespace GSM.Application.Achievements;

public sealed class UploadAchievementIconHandler(
    ICurrentUserService currentUser,
    IAchievementRepository achievements,
    IFileStorage storage,
    IUnitOfWork unitOfWork)
{
    public async Task<AchievementResponse> HandleAsync(
        Guid achievementId, FileToStore file, CancellationToken cancellationToken)
    {
        _ = currentUser.RequireGymId();
        ImageUploadRules.EnsureValid(file);

        var achievement = await achievements.GetByIdAsync(achievementId, cancellationToken)
            ?? throw new NotFoundException("Logro no encontrado.");

        var previous = achievement.IconUrl;
        var url = await storage.SaveAsync(file, "achievement-icons", cancellationToken);

        achievement.SetIcon(url);
        await unitOfWork.SaveChangesAsync(cancellationToken);

        if (!string.IsNullOrWhiteSpace(previous))
        {
            await storage.DeleteAsync(previous, cancellationToken);
        }

        return AchievementMapper.ToResponse(achievement);
    }
}
