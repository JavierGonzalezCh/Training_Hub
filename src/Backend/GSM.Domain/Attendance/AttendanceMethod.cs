namespace GSM.Domain.Attendance;

public enum AttendanceMethod
{
    Qr,
    Manual,

    /// <summary>Entrada con el documento digitado en el kiosco (A4).</summary>
    Document,
}
