using MudBlazor;

namespace GSM.Panel.UI.Pages.Settings;

/// <summary>
/// A8: sin registro de entrada en la puerta (check-in con QR y entrada con documento apagados), el miembro cuenta su día
/// al iniciar el entrenamiento en la app. Si además el gym muestra logros o progreso (racha), el dueño debe saber que
/// cualquiera puede marcarlo desde su casa. Se avisa al guardar el cambio que lleva a esa situación, en Módulos (QR,
/// logros, progreso) y en Check-in (documento); no cada vez que se guarda estando ya en ella.
/// </summary>
public static class RoutineEntryRisk
{
    /// <param name="RemoteTraining">T4: los miembros entrenan fuera de la sede; marcar desde casa es lo esperado, no un riesgo.</param>
    public sealed record State(
        bool CheckinQr, bool ByDocument, bool ShowGamification, bool ShowProgress, bool RemoteTraining = false)
    {
        public bool AtRisk => !CheckinQr && !ByDocument && !RemoteTraining && (ShowGamification || ShowProgress);
    }

    public static bool ShouldWarn(State before, State after) => after.AtRisk && !before.AtRisk;

    public const string Title = "Sin registro de entrada en la puerta";

    public const string Message =
        "Tus miembros contarán su día al tocar «Iniciar rutina» en la app, así siguen sumando racha y logros. " +
        "Pero cualquiera podría marcarlo desde su casa, sin venir al gimnasio. " +
        "Para evitarlo, deja encendido el check-in con QR o la entrada con documento, o indica en Check-in que tus miembros entrenan fuera de tu sede.";

    /// <summary>Pide confirmación; <c>true</c> si el dueño decide guardar igual.</summary>
    public static async Task<bool> ConfirmAsync(IDialogService dialogs)
        => await dialogs.ShowMessageBox(Title, Message, yesText: "Guardar así", cancelText: "Volver") == true;
}
