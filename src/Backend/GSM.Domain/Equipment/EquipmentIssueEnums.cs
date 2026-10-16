namespace GSM.Domain.Equipment;

/// <summary>
/// Motivo de un reporte de falla: lo que permite reportar con dos toques (Equipment §3.5). Label en Postgres:
/// <c>equipment.equipment_issue_reason</c> (snake_case). <see cref="Unsafe"/> sale como aviso urgente y avisa al Owner por correo.
/// </summary>
public enum EquipmentIssueReason
{
    NotWorking,
    DamagedPart,
    StrangeNoise,
    MissingPart,
    Dirty,
    Unsafe,
    Other,
}

/// <summary>Estado de un reporte. <c>Resolved</c> y <c>Dismissed</c> lo cierran; el miembro solo recibe aviso al resolverse.</summary>
public enum EquipmentIssueStatus
{
    Open,
    InProgress,
    Resolved,
    Dismissed,
}

/// <summary>Quién reportó: un miembro desde la app o alguien del equipo desde el panel.</summary>
public enum IssueReporterRole
{
    Member,
    Staff,
}
