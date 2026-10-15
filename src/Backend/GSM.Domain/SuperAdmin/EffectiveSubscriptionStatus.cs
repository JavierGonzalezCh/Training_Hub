namespace GSM.Domain.SuperAdmin;

/// <summary>
/// Estado comercial efectivo de un gym como cliente del SaaS. No se persiste: se deriva de
/// <see cref="GymSubscription.DeriveStatus"/> comparando <c>paid_through</c> con el día de hoy,
/// más el estado manual del gym. Mismo criterio que las membresías <c>expired</c> de la etapa 4.
/// </summary>
public enum EffectiveSubscriptionStatus
{
    /// <summary>Periodo de prueba, hasta <c>trial_ends_on</c>. Acceso total.</summary>
    Trial,

    /// <summary>
    /// Derivado: los <c>TrialGraceDays</c> siguientes al fin de la prueba sin pago confirmado. Acceso total,
    /// con el letrero "Tu prueba terminó". Pasados, <see cref="Disabled"/>.
    /// </summary>
    TrialEnded,

    /// <summary>Suscripción al día. Acceso total.</summary>
    Active,

    /// <summary>Vencida hace ≤ 15 días. Acceso total, con aviso visible.</summary>
    Overdue,

    /// <summary>Vencida hace > 15 días. Solo lectura.</summary>
    Disabled,

    /// <summary>Suspensión manual por un superadmin. Sin acceso. Nunca derivado del tiempo.</summary>
    Suspended,
}
