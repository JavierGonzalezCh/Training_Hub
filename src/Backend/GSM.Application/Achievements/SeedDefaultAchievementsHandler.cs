using GSM.Application.Achievements.Abstractions;
using GSM.Application.Common.Abstractions;
using GSM.Application.Common.Events;
using GSM.Application.Gyms.Events;
using GSM.Domain.Achievements;
using Microsoft.Extensions.Logging;

namespace GSM.Application.Achievements;

/// <summary>Al crear un gym, siembra el catálogo de logros de su tipo. Reacciona a <see cref="GymCreated"/>.</summary>
public sealed class SeedDefaultAchievementsHandler(
    IAchievementRepository achievements,
    IUnitOfWork unitOfWork,
    ILogger<SeedDefaultAchievementsHandler> logger) : IDomainEventHandler<GymCreated>
{
    public async Task HandleAsync(GymCreated domainEvent, CancellationToken cancellationToken)
    {
        // Idempotente: un reintento del evento no vuelve a sembrar (ni choca con el índice único del nombre).
        var existing = await achievements.GetNamesForGymAsync(domainEvent.GymId, cancellationToken);
        foreach (var seed in AchievementTemplates.For(domainEvent.Type).Where(s => !existing.Contains(s.Name)))
        {
            achievements.Add(Achievement.Create(
                domainEvent.GymId, seed.Name, seed.Description, null, seed.Type, seed.Value));
        }

        await unitOfWork.SaveChangesAsync(cancellationToken);
        logger.LogInformation(
            "Logros de {Type} sembrados para el gym {GymId}", domainEvent.Type, domainEvent.GymId);
    }
}
