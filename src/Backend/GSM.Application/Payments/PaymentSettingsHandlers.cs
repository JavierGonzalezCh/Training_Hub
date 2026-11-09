using GSM.Application.Common.Abstractions;
using GSM.Application.Common.Events;
using GSM.Application.Gyms.Events;
using GSM.Application.Payments.Abstractions;
using GSM.Application.Payments.Contracts;
using GSM.Domain.Payments;
using Microsoft.Extensions.Logging;

namespace GSM.Application.Payments;

public sealed class GetPaymentSettingsHandler(ICurrentUserService currentUser, IPaymentRepository payments)
{
    public async Task<PaymentSettingsResponse> HandleAsync(CancellationToken cancellationToken)
    {
        var gymId = currentUser.RequireGymId();
        var current = await payments.GetSettingsAsync(gymId, cancellationToken) ?? PaymentSettings.Default(gymId);
        return new PaymentSettingsResponse(current.UseCashDrawer);
    }
}

/// <summary>Ajustes de pagos del gym. Sin fila valen los de por defecto; la primera edición la crea.</summary>
public sealed class UpdatePaymentSettingsHandler(
    ICurrentUserService currentUser,
    IPaymentRepository payments,
    IUnitOfWork unitOfWork,
    ILogger<UpdatePaymentSettingsHandler> logger)
{
    public async Task<PaymentSettingsResponse> HandleAsync(
        UpdatePaymentSettingsRequest request, CancellationToken cancellationToken)
    {
        var gymId = currentUser.RequireGymId();
        var current = await payments.GetSettingsAsync(gymId, cancellationToken);
        if (current is null)
        {
            current = PaymentSettings.Default(gymId);
            payments.Add(current);
        }

        current.Update(request.UseCashDrawer);
        await unitOfWork.SaveChangesAsync(cancellationToken);

        logger.LogInformation("Ajustes de pagos del gym {GymId}: usa caja {UseCashDrawer}", gymId, current.UseCashDrawer);
        return new PaymentSettingsResponse(current.UseCashDrawer);
    }
}

/// <summary>
/// Al crear un gym, aplica la parte de pagos de la plantilla de su tipo. Solo escribe si no hay fila de ajustes: reintentarlo
/// no pisa lo que el dueño ya cambió.
/// </summary>
public sealed class GymCreatedAppliesPaymentTemplateHandler(
    IPaymentRepository payments,
    IUnitOfWork unitOfWork,
    ILogger<GymCreatedAppliesPaymentTemplateHandler> logger) : IDomainEventHandler<GymCreated>
{
    public async Task HandleAsync(GymCreated domainEvent, CancellationToken cancellationToken)
    {
        if (await payments.GetSettingsAsync(domainEvent.GymId, cancellationToken) is not null)
        {
            return;
        }

        var row = PaymentSettings.Default(domainEvent.GymId);
        row.Update(PaymentTemplates.For(domainEvent.Type).UseCashDrawer);
        payments.Add(row);

        await unitOfWork.SaveChangesAsync(cancellationToken);
        logger.LogInformation(
            "Plantilla de pagos {Type} aplicada al gym {GymId}", domainEvent.Type, domainEvent.GymId);
    }
}
