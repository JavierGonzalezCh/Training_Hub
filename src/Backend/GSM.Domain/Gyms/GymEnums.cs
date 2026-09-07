namespace GSM.Domain.Gyms;

/// <summary>Plan comercial del gym (SaaS). El módulo de facturación llega en una fase futura.</summary>
public enum GymPlan
{
    Basic,
    Pro,
    Enterprise,
}

/// <summary>Estado de la cuenta del gym como cliente del SaaS.</summary>
public enum GymStatus
{
    Trial,
    Active,
    Suspended,
}
