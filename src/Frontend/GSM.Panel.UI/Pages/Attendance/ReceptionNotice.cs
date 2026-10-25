using GSM.UI.Shared.Formatting;

namespace GSM.Panel.UI.Pages.Attendance;

/// <summary>
/// Aviso de acceso redactado para recepción. El servidor manda <c>Notice</c> en segunda persona porque
/// es el mismo letrero del kiosco y de la app ("Tu plan venció…"); en el panel lo lee el staff, así que
/// se rearma en tercera persona con los datos estructurados de la tarjeta.
/// </summary>
public static class ReceptionNotice
{
    /// <param name="kind">El <c>Kind</c> del acceso: <c>covered</c>, <c>expiring</c>, <c>grace</c> o <c>blocked</c>.</param>
    /// <param name="fallback">El aviso del servidor, para los casos que aquí no se reescriben.</param>
    public static string? For(
        string? kind, DateOnly? endsOn, int? daysLeft, int? graceDaysLeft, int? entriesLeft, int? entriesTotal,
        string? fallback)
    {
        switch (kind)
        {
            case "grace":
                var since = endsOn is { } ended ? $"Venció el {GsmFormat.DayShort(ended)}. " : "Su plan venció. ";
                var grace = graceDaysLeft switch
                {
                    null => "Está en gracia.",
                    <= 0 => "Hoy es su último día de gracia.",
                    1 => "Está en gracia: puede entrar 1 día más sin renovar.",
                    var n => $"Está en gracia: puede entrar {n} días más sin renovar.",
                };
                return $"{since}{grace} Ofrécele renovar.";

            case "expiring" when entriesTotal is { } total:
                var until = endsOn is { } passEnd ? $" (vence el {GsmFormat.DayShort(passEnd)})" : string.Empty;
                var left = entriesLeft ?? 0;
                return $"{(left == 1 ? "Le queda 1 entrada" : $"Le quedan {left} entradas")} de {total}{until}. Ofrécele renovar.";

            case "expiring" when daysLeft is { } days:
                var when = days switch
                {
                    <= 0 => "Su plan vence hoy.",
                    1 => "Su plan vence mañana.",
                    _ => $"Su plan vence en {days} días{(endsOn is { } end ? $" (el {GsmFormat.DayShort(end)})" : string.Empty)}.",
                };
                return $"{when} Ofrécele renovar.";

            default:
                return fallback;
        }
    }
}
