using GSM.Application.Achievements.Contracts;
using GSM.Application.Achievements.Public;
using GSM.Domain.Achievements;
using Riok.Mapperly.Abstractions;

namespace GSM.Application.Achievements;

[Mapper(RequiredMappingStrategy = RequiredMappingStrategy.Target)]
public static partial class AchievementMapper
{
    public static partial AchievementResponse ToResponse(Achievement achievement);

    public static partial MemberAchievementItem ToItem(MemberAchievementView view);
}
