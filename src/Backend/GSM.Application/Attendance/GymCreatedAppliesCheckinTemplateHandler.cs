using GSM.Application.Attendance.Abstractions;
using GSM.Application.Common.Abstractions;
using GSM.Application.Common.Events;
using GSM.Application.Gyms.Events;
using GSM.Domain.Attendance;
using Microsoft.Extensions.Logging;

namespace GSM.Application.Attendance;

/// <summary>
/// Al crear un gym, aplica la parte de check-in de la plantilla de su tipo. Solo escribe si el gym aún no tiene fila
/// de ajustes (sin fila = nadie la ha tocado), así que reintentarlo no pisa lo que el dueño ya cambió.
/// </summary>
public sealed class GymCreatedAppliesCheckinTemplateHandler(
    ICheckinSettingsRepository settings,
    IUnitOfWork unitOfWork,
    ILogger<GymCreatedAppliesCheckinTemplateHandler> logger) : IDomainEventHandler<GymCreated>
{
    public async Task HandleAsync(GymCreated domainEvent, CancellationToken cancellationToken)
    {
        if (await settings.GetAsync(domainEvent.GymId, cancellationToken) is not null)
        {
            return;
        }

        var template = CheckinTemplates.For(domainEvent.Type);
        var row = CheckinSettings.Default(domainEvent.GymId);
        row.Update(
            template.Mode, assumedStayMinutes: template.AssumedStayMinutes,
            remoteTraining: template.RemoteTraining, trainingEntryTrigger: template.TrainingEntryTrigger);
        settings.Add(row);

        await unitOfWork.SaveChangesAsync(cancellationToken);
        logger.LogInformation(
            "Plantilla de check-in {Type} aplicada al gym {GymId}", domainEvent.Type, domainEvent.GymId);
    }
}
