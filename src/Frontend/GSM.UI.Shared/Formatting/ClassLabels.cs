namespace GSM.UI.Shared.Formatting;

/// <summary><c>ClassDayOfWeek</c> del backend (Monday=0…Sunday=6) en español.</summary>
public static class ClassLabels
{
    public static readonly (int Value, string Label)[] DaysOfWeek =
    [
        (0, "Lunes"), (1, "Martes"), (2, "Miércoles"), (3, "Jueves"),
        (4, "Viernes"), (5, "Sábado"), (6, "Domingo"),
    ];

    private static readonly Dictionary<string, string> DayByName = new()
    {
        ["Monday"] = "Lunes", ["Tuesday"] = "Martes", ["Wednesday"] = "Miércoles",
        ["Thursday"] = "Jueves", ["Friday"] = "Viernes", ["Saturday"] = "Sábado", ["Sunday"] = "Domingo",
    };

    public static string DayLabel(string name) => DayByName.GetValueOrDefault(name, name);

    /// <summary>Nombre del enum (<c>"Monday"</c>…) → valor entero para el <c>&lt;select&gt;</c> de edición.</summary>
    public static int ToValue(string name) => name switch
    {
        "Monday" => 0, "Tuesday" => 1, "Wednesday" => 2, "Thursday" => 3,
        "Friday" => 4, "Saturday" => 5, "Sunday" => 6, _ => 0,
    };

    public static string DayLabel(int value) => value is >= 0 and < 7 ? DaysOfWeek[value].Label : value.ToString();

    public static string SessionStatusLabel(string status) => status switch
    {
        "Scheduled" => "Programada",
        "Cancelled" => "Cancelada",
        "Completed" => "Realizada",
        _ => status,
    };

    public static string BookingStatusLabel(string status) => status switch
    {
        "Booked" => "Reservado",
        "Cancelled" => "Cancelado",
        "Attended" => "Asistió",
        "NoShow" => "No asistió",
        _ => status,
    };
}
