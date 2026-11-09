using GSM.Domain.Gyms;

namespace GSM.Application.Payments;

/// <summary>Valores iniciales de pagos por tipo de negocio (docs/GSM-tipos-de-negocio.md §4).</summary>
public sealed record PaymentTemplate(bool UseCashDrawer);

public static class PaymentTemplates
{
    /// <summary>El entrenador personal no maneja caja; el resto lleva el efectivo por turnos.</summary>
    public static PaymentTemplate For(GymType type) => type switch
    {
        GymType.PersonalTrainer => new(UseCashDrawer: false),
        _ => new(UseCashDrawer: true),
    };
}
