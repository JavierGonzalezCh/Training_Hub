using GSM.Client.Generated;
using GSM.UI.Shared.Formatting;

namespace GSM.UI.Shared.Routines;

/// <summary>
/// Textos de disponibilidad de un ejercicio según las máquinas del gym (Equipment §5 y §6). El servidor manda los
/// datos (<c>ExerciseEquipmentInfo</c>); aquí solo se redactan. Sirven al editor de rutinas (aviso que no impide
/// guardar) y a la pantalla del ejercicio en la app del miembro.
/// </summary>
public static class EquipmentNotice
{
    public const string Available = "Available";
    public const string Maintenance = "Maintenance";
    public const string OutOfService = "OutOfService";

    /// <summary>
    /// "Prensa 45° en mantenimiento hasta el 15 oct", o <c>null</c> si el ejercicio se puede hacer. Si el ejercicio
    /// no tiene máquinas ligadas pero el gym no tiene el tipo de equipo, lo dice. Con varias máquinas, avisa
    /// solo cuando ninguna está disponible.
    /// </summary>
    public static string? Warning(ExerciseEquipmentInfo? info)
    {
        if (info is null || info.IsAvailable)
        {
            return null;
        }

        var machines = (info.Machines ?? []).ToList();
        if (machines.Count == 0)
        {
            return "El gym no tiene el equipo que pide este ejercicio.";
        }

        var first = machines[0];
        if (machines.Count > 1)
        {
            return $"Ninguna de las {machines.Count} máquinas de este ejercicio está disponible ({first.Name} {StatusPhrase(first.Status)}).";
        }

        var text = $"{first.Name} {StatusPhrase(first.Status)}";
        return first.ExpectedBackOn is { } back ? $"{text} hasta el {GsmFormat.DayShort(back)}" : text;
    }

    /// <summary>La máquina que se muestra para el ejercicio: la primera disponible o, si no hay, la primera.</summary>
    public static EquipmentMachineInfo? Primary(ExerciseEquipmentInfo? info)
        => info?.Machines?.FirstOrDefault(m => m.Status == Available) ?? info?.Machines?.FirstOrDefault();

    public static string StatusPhrase(string status) => status switch
    {
        Maintenance => "en mantenimiento",
        OutOfService => "fuera de servicio",
        _ => "disponible",
    };

    /// <summary>Línea de ubicación: "Zona de pierna", o <c>null</c> si la máquina no tiene zona.</summary>
    public static string? Location(EquipmentMachineInfo? machine)
        => string.IsNullOrWhiteSpace(machine?.Zone) ? null : machine!.Zone;
}
