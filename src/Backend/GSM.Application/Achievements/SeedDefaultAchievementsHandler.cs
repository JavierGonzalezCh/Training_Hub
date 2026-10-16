using GSM.Application.Achievements.Abstractions;
using GSM.Application.Common.Abstractions;
using GSM.Application.Common.Events;
using GSM.Application.Gyms.Events;
using GSM.Domain.Achievements;
using Microsoft.Extensions.Logging;

namespace GSM.Application.Achievements;

/// <summary>Al crear un gym, siembra los logros por defecto. Reacciona a <see cref="GymCreated"/>.</summary>
public sealed class SeedDefaultAchievementsHandler(
    IAchievementRepository achievements,
    IUnitOfWork unitOfWork,
    ILogger<SeedDefaultAchievementsHandler> logger) : IDomainEventHandler<GymCreated>
{
    private static readonly (string Name, string Description, AchievementConditionType Type, int Value)[] Defaults =
    [
        ("Primera visita", "Tu primer check-in", AchievementConditionType.Visits, 1),
        ("10 visitas", "10 check-ins acumulados", AchievementConditionType.Visits, 10),
        ("50 visitas", "50 check-ins acumulados", AchievementConditionType.Visits, 50),
        ("Racha de 7 días", "7 días seguidos entrenando", AchievementConditionType.Streak, 7),
        ("Racha de 30 días", "30 días seguidos entrenando", AchievementConditionType.Streak, 30),
        ("1 año con nosotros", "12 meses como miembro", AchievementConditionType.TenureMonths, 12),
        ("Primer récord", "Batiste un récord personal", AchievementConditionType.PersonalRecords, 1),
        ("Primer amigo", "Un amigo que referiste ya se inscribió", AchievementConditionType.Referrals, 1),
    ];

    public async Task HandleAsync(GymCreated domainEvent, CancellationToken cancellationToken)
    {
        foreach (var (name, description, type, value) in Defaults)
        {
            achievements.Add(Achievement.Create(domainEvent.GymId, name, description, null, type, value));
        }

        await unitOfWork.SaveChangesAsync(cancellationToken);
        logger.LogInformation("Logros por defecto sembrados para el gym {GymId}", domainEvent.GymId);
    }
}
