using GSM.Application.Identity.Abstractions;
using GSM.Application.SuperAdmin.Public;
using GSM.Domain.SuperAdmin;

namespace GSM.Infrastructure.SuperAdmin;

/// <summary>
/// SuperAdmin implementa el puerto de login de Identity con su estado comercial efectivo. Un gym sin
/// suscripción no está suspendido: ante el dato faltante se deja pasar (etapa 11.2).
/// </summary>
public sealed class SubscriptionLoginGate(ISubscriptionReader subscriptions) : IGymLoginGate
{
    public async Task<bool> IsSuspendedAsync(Guid gymId, CancellationToken cancellationToken = default)
        => await subscriptions.GetEffectiveStatusAsync(gymId, cancellationToken)
            == EffectiveSubscriptionStatus.Suspended;
}
