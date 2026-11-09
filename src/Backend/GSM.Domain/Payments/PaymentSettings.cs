using GSM.Domain.Common;
using GSM.Domain.Common.Exceptions;

namespace GSM.Domain.Payments;

/// <summary>
/// Ajustes de pagos del gym. Uno por gym; si no hay fila valen los de <see cref="Default"/> y no se persisten hasta el
/// primer cambio (configuración perezosa).
/// </summary>
public sealed class PaymentSettings : BaseEntity, ITenantEntity
{
    private PaymentSettings()
    {
    }

    public Guid GymId { get; private set; }

    /// <summary>
    /// El negocio lleva el efectivo por turnos de caja (base, movimientos y arqueo). Apagado: el efectivo se registra sin
    /// turno y no mueve ninguna caja. Encendido por defecto.
    /// </summary>
    public bool UseCashDrawer { get; private set; } = true;

    public static PaymentSettings Default(Guid gymId)
    {
        if (gymId == Guid.Empty)
        {
            throw new ValidationDomainException("Gym inválido.");
        }

        return new PaymentSettings { GymId = gymId, UseCashDrawer = true };
    }

    public void Update(bool useCashDrawer) => UseCashDrawer = useCashDrawer;
}
