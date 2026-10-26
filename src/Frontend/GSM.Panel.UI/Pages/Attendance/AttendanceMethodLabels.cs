namespace GSM.Panel.UI.Pages.Attendance;

/// <summary>Cómo se registró una entrada, tal como la lee recepción.</summary>
public static class AttendanceMethodLabels
{
    public static string Of(string? method) => method switch
    {
        "Qr" => "QR",
        "Document" => "Documento",
        // A8: el miembro la marcó al iniciar su entrenamiento (gym sin registro en la puerta).
        "Routine" => "Rutina",
        _ => "Manual",
    };
}
