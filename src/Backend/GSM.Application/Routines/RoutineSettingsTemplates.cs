using GSM.Domain.Gyms;
using GSM.Domain.Routines;

namespace GSM.Application.Routines;

/// <summary>Valores iniciales de rutinas por tipo de negocio (docs/GSM-tipos-de-negocio.md §4.4).</summary>
public sealed record RoutineSettingsTemplate(int ReviewWeeks);

public static class RoutineSettingsTemplates
{
    /// <summary>El entrenador personal revisa las rutinas de sus clientes cada 4 semanas; el resto, cada 6.</summary>
    public static RoutineSettingsTemplate For(GymType type) => type switch
    {
        GymType.PersonalTrainer => new(4),
        _ => new(RoutineSettings.DefaultReviewWeeks),
    };
}
