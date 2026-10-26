using GSM.Application.Common.Abstractions;
using GSM.Application.Common.Events;
using GSM.Application.Gyms.Events;
using GSM.Application.Routines.Abstractions;
using GSM.Domain.Routines;
using Microsoft.Extensions.Logging;

namespace GSM.Application.Routines;

/// <summary>
/// Al crear un gym, aplica la parte de rutinas de la plantilla de su tipo. Solo escribe si no hay fila de ajustes:
/// reintentarlo no pisa lo que el dueño ya cambió.
/// </summary>
public sealed class GymCreatedAppliesRoutineTemplateHandler(
    IRoutineRepository routines,
    IUnitOfWork unitOfWork,
    ILogger<GymCreatedAppliesRoutineTemplateHandler> logger) : IDomainEventHandler<GymCreated>
{
    public async Task HandleAsync(GymCreated domainEvent, CancellationToken cancellationToken)
    {
        if (await routines.GetSettingsAsync(domainEvent.GymId, cancellationToken) is not null)
        {
            return;
        }

        var row = RoutineSettings.Default(domainEvent.GymId);
        row.Update(RoutineSettingsTemplates.For(domainEvent.Type).ReviewWeeks);
        routines.Add(row);

        await unitOfWork.SaveChangesAsync(cancellationToken);
        logger.LogInformation(
            "Plantilla de rutinas {Type} aplicada al gym {GymId}", domainEvent.Type, domainEvent.GymId);
    }
}
