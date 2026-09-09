namespace GSM.Domain.Classes;

/// <summary>
/// Estado de una ocurrencia concreta de clase. Enum nativo de Postgres
/// (<c>classes.class_session_status</c>).
/// </summary>
public enum ClassSessionStatus
{
    Scheduled,
    Cancelled,
}
