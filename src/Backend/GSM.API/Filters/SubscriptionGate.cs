using GSM.Domain.SuperAdmin;

namespace GSM.API.Filters;

/// <summary>
/// Decisión pura del bloqueo por estado comercial: sin <c>HttpContext</c>, para poder probarla
/// exhaustivamente. <see cref="SubscriptionGateFilter"/> solo traduce la petición a estas primitivas
/// y el resultado a un <c>403</c>.
/// </summary>
public static class SubscriptionGate
{
    public enum Decision
    {
        /// <summary>Se deja pasar.</summary>
        Allow,

        /// <summary>Inhabilitado: solo lectura. Se rechaza por ser escritura.</summary>
        BlockedDisabled,

        /// <summary>Suspendido: se rechaza todo.</summary>
        BlockedSuspended,
    }

    /// <param name="status">Estado efectivo del gym, o <c>null</c> si no tiene suscripción.</param>
    /// <param name="isSuperAdmin">El superadmin siempre queda exento.</param>
    /// <param name="hasGym">Si la petición no trae gym en contexto no hay nada que evaluar aquí.</param>
    /// <param name="isReadMethod"><c>GET</c>/<c>HEAD</c>: lo único que un gym <c>disabled</c> puede hacer.</param>
    /// <param name="isExempt">Endpoint marcado con <see cref="SubscriptionGateExemptAttribute"/>.</param>
    public static Decision Evaluate(
        EffectiveSubscriptionStatus? status,
        bool isSuperAdmin,
        bool hasGym,
        bool isReadMethod,
        bool isExempt)
    {
        if (isSuperAdmin || !hasGym || isExempt || status is null)
        {
            return Decision.Allow;
        }

        return status switch
        {
            EffectiveSubscriptionStatus.Suspended => Decision.BlockedSuspended,
            EffectiveSubscriptionStatus.Disabled when !isReadMethod => Decision.BlockedDisabled,
            _ => Decision.Allow,
        };
    }
}
