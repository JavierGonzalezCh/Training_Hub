namespace GSM.Domain.Gyms;

/// <summary>
/// Tipo de negocio del tenant. Es una plantilla de arranque y una etiqueta, no un modo: ningún caso de uso
/// pregunta por el tipo para decidir un comportamiento (docs/GSM-tipos-de-negocio.md §2).
/// </summary>
public enum GymType
{
    Gym,
    Box,
    Studio,
    PersonalTrainer,
    Academy,
    Other,
}

/// <summary>Estado de la cuenta del gym como cliente del SaaS.</summary>
public enum GymStatus
{
    Trial,
    Active,
    Suspended,
}
