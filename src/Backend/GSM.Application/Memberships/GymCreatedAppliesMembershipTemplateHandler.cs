using GSM.Application.Common.Abstractions;
using GSM.Application.Common.Events;
using GSM.Application.Gyms.Events;
using GSM.Application.Memberships.Abstractions;
using GSM.Domain.Memberships;
using Microsoft.Extensions.Logging;

namespace GSM.Application.Memberships;

/// <summary>
/// Al crear un gym, aplica la parte de membresías de la plantilla de su tipo. Solo escribe si no hay fila de
/// ajustes: reintentarlo no pisa lo que el dueño ya cambió.
/// </summary>
public sealed class GymCreatedAppliesMembershipTemplateHandler(
    IMembershipRepository memberships,
    IUnitOfWork unitOfWork,
    ILogger<GymCreatedAppliesMembershipTemplateHandler> logger) : IDomainEventHandler<GymCreated>
{
    public async Task HandleAsync(GymCreated domainEvent, CancellationToken cancellationToken)
    {
        if (await memberships.GetSettingsAsync(domainEvent.GymId, cancellationToken) is not null)
        {
            return;
        }

        var template = MembershipTemplates.For(domainEvent.Type);
        var row = MembershipSettings.Default(domainEvent.GymId);
        row.Update(template.GraceDays, template.ExpiringDays, template.EnrollmentFee);
        memberships.Add(row);

        await unitOfWork.SaveChangesAsync(cancellationToken);
        logger.LogInformation(
            "Plantilla de membresías {Type} aplicada al gym {GymId}", domainEvent.Type, domainEvent.GymId);
    }
}
