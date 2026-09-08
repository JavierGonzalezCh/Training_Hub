using GSM.Application.Achievements.Contracts;
using GSM.Application.Achievements.Public;
using GSM.Application.Common.Abstractions;
using GSM.Application.Members.Public;
using GSM.Domain.Common.Exceptions;
using GSM.Domain.Identity;

namespace GSM.Application.Achievements;

public sealed class GetMemberAchievementsHandler(
    ICurrentUserService currentUser,
    IMemberReader members,
    IAchievementReader achievements)
{
    public async Task<IReadOnlyList<MemberAchievementItem>> HandleAsync(
        Guid memberId, CancellationToken cancellationToken)
    {
        var gymId = currentUser.RequireGymId();

        if (currentUser.Role == UserRole.Member)
        {
            var self = await members.ResolveByUserIdAsync(currentUser.UserId ?? Guid.Empty, cancellationToken);
            if (self is null || self.Id != memberId)
            {
                throw new ForbiddenDomainException("No puedes ver los logros de otro miembro.");
            }
        }

        var member = await members.GetAsync(memberId, cancellationToken)
            ?? throw new NotFoundException("Miembro no encontrado.");

        var views = await achievements.GetForMemberAsync(member.Id, gymId, cancellationToken);
        return views.Select(AchievementMapper.ToItem).ToList();
    }
}

public sealed class GetMyAchievementsHandler(
    ICurrentUserService currentUser,
    IMemberReader members,
    IAchievementReader achievements)
{
    public async Task<IReadOnlyList<MemberAchievementItem>> HandleAsync(CancellationToken cancellationToken)
    {
        var gymId = currentUser.RequireGymId();
        var self = await members.ResolveByUserIdAsync(currentUser.UserId ?? Guid.Empty, cancellationToken)
            ?? throw new ForbiddenDomainException("Tu cuenta no está vinculada a un miembro.");

        var views = await achievements.GetForMemberAsync(self.Id, gymId, cancellationToken);
        return views.Select(AchievementMapper.ToItem).ToList();
    }
}
