using GSM.Domain.Attendance;
using GSM.Domain.Gyms;

namespace GSM.Application.Attendance;

/// <summary>Valores iniciales de check-in por tipo de negocio (docs/GSM-tipos-de-negocio.md §4.2).</summary>
public sealed record CheckinTemplate(
    CheckinMode Mode,
    int AssumedStayMinutes,
    bool RemoteTraining = false,
    TrainingEntryTrigger TrainingEntryTrigger = TrainingEntryTrigger.Start);

public static class CheckinTemplates
{
    /// <summary>
    /// Box, estudio y academia usan el cartel fijo (sus miembros llegan juntos a la hora de la clase) y una
    /// permanencia de clase. El resto conserva los valores de siempre.
    /// </summary>
    public static CheckinTemplate For(GymType type) => type switch
    {
        GymType.Box => new(CheckinMode.Static, 60),
        GymType.Studio => new(CheckinMode.Static, 60),
        GymType.Academy => new(CheckinMode.Static, 90),
        // Sin puerta ni sede: entrena donde esté y el día cuenta con el primer ejercicio registrado.
        GymType.PersonalTrainer => new(
            CheckinMode.Rotating, CheckinSettings.DefaultAssumedStayMinutes,
            RemoteTraining: true, TrainingEntryTrigger: TrainingEntryTrigger.FirstLog),
        _ => new(CheckinMode.Rotating, CheckinSettings.DefaultAssumedStayMinutes),
    };
}
