using GSM.Client.Generated;

namespace GSM.UI.Shared.Auth;

/// <summary>
/// Cache del bootstrap (<c>GET /api/auth/me</c>): rol, gym, branding, feature flags y suscripción.
/// Es la fuente para el tema por gym, el menú por rol y el banner de suscripción.
/// Scoped: se llena tras el login y se limpia en el logout.
/// </summary>
public sealed class GymContext
{
    public MeResponse? Me { get; private set; }

    public bool IsLoaded => Me is not null;

    public MeGym? Gym => Me?.Gym;

    public MeFeatureFlags? FeatureFlags => Me?.FeatureFlags;

    public SubscriptionInfo? Subscription => Me?.Subscription;

    public string? Role => Me?.Role;

    /// <summary>Estado efectivo de la suscripción, normalizado. <c>null</c> = el gym no tiene suscripción.</summary>
    public SubscriptionState SubscriptionState => (Subscription?.Status ?? string.Empty).ToLowerInvariant() switch
    {
        "overdue" => SubscriptionState.Overdue,
        "disabled" => SubscriptionState.Disabled,
        "suspended" => SubscriptionState.Suspended,
        "" => SubscriptionState.None,
        _ => SubscriptionState.Active,
    };

    /// <summary>
    /// <c>true</c> cuando la UI debe ocultar las acciones de escritura: gym <c>Disabled</c>
    /// (solo lectura) o sesión de impersonación.
    /// </summary>
    public bool ReadOnly { get; private set; }

    public event Action? Changed;

    public void Set(MeResponse me, bool impersonating)
    {
        Me = me;
        ReadOnly = impersonating || SubscriptionState == SubscriptionState.Disabled;
        Changed?.Invoke();
    }

    public void Clear()
    {
        Me = null;
        ReadOnly = false;
        Changed?.Invoke();
    }
}

public enum SubscriptionState
{
    /// <summary>El gym no tiene suscripción: no se muestra aviso (el backend tampoco bloquea).</summary>
    None,
    Active,
    Overdue,
    Disabled,
    Suspended,
}
