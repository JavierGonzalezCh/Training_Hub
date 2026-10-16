namespace GSM.Application.Common.Events;

/// <summary>
/// Alguien atendió un aviso de acción del staff (Notifications). El módulo dueño de lo que originó el
/// aviso reacciona según <paramref name="Kind"/> (p. ej. Attendance marca el intento como visto).
/// Vive en Common para que ese módulo no dependa de Notifications.
/// </summary>
public sealed record ActionNoticeResolved(
    Guid GymId,
    string Kind,
    Guid ReferenceId,
    Guid ResolvedBy,
    DateTimeOffset ResolvedAt) : IDomainEvent;

public static class ActionNoticeKinds
{
    public const string CheckinRejected = "checkin_rejected";

    /// <summary>E4: aviso de reporte de falla de una máquina (la referencia es el reporte que encabeza el aviso).</summary>
    public const string EquipmentIssue = "equipment_issue";
}
