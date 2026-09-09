using GSM.Domain.SuperAdmin;

namespace GSM.API.Filters;

/// <summary>
/// Decisión pura del bloqueo transversal de la API: sin <c>HttpContext</c>, para poder probarla
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

        /// <summary>Token de impersonación: solo lectura, se rechaza la escritura.</summary>
        BlockedImpersonation,
    }

    /// <param name="status">Estado efectivo del gym, o <c>null</c> si no tiene suscripción.</param>
    /// <param name="isSuperAdmin">El superadmin (no impersonando) queda exento del bloqueo por estado.</param>
    /// <param name="isImpersonating">Token de impersonación: solo <c>GET</c>, aunque sea superadmin.</param>
    /// <param name="hasGym">Si la petición no trae gym en contexto no hay estado que evaluar.</param>
    /// <param name="isReadMethod"><c>GET</c>/<c>HEAD</c>.</param>
    /// <param name="isExempt">Endpoint marcado con <see cref="SubscriptionGateExemptAttribute"/>.</param>
    public static Decision Evaluate(
        EffectiveSubscriptionStatus? status,
        bool isSuperAdmin,
        bool isImpersonating,
        bool hasGym,
        bool isReadMethod,
        bool isExempt)
    {
        if (isExempt)
        {
            return Decision.Allow;
        }

        // La impersonación es solo lectura, y manda sobre la exención del superadmin.
        if (isImpersonating && !isReadMethod)
        {
            return Decision.BlockedImpersonation;
        }

        if (isSuperAdmin || !hasGym || status is null)
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
