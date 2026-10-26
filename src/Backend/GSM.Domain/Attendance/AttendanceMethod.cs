namespace GSM.Domain.Attendance;

public enum AttendanceMethod
{
    Qr,
    Manual,

    /// <summary>Entrada con el documento digitado en el kiosco (A4).</summary>
    Document,

    /// <summary>
    /// El miembro se registró solo al iniciar su entrenamiento (A8): solo existe cuando el gym no tiene ningún
    /// registro de entrada (sin QR ni documento). Label en Postgres: <c>routine</c>.
    /// </summary>
    Routine,
}
