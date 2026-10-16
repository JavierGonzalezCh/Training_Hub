namespace GSM.Panel.UI.Onboarding;

/// <summary>Claves de los pasos del onboarding, iguales a las que devuelve <c>GET /api/gym/onboarding</c>.</summary>
public static class OnboardingKeys
{
    public const string Gym = "gym";
    public const string Location = "location";
    public const string Hours = "hours";
    public const string Equipment = "equipment";
    public const string Plans = "plans";
    public const string Entry = "entry";
    public const string Team = "team";

    /// <summary>Pendiente opcional de la lista «Para completar»: aún no hay miembros.</summary>
    public const string Members = "members";
}
