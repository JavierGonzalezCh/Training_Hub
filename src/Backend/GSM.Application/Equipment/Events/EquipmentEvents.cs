using GSM.Application.Common.Events;
using GSM.Domain.Equipment;

namespace GSM.Application.Equipment.Events;

/// <summary>
/// Se reportó una falla (E4). Notifications crea el aviso de acción del staff (agrupado por máquina mientras haya
/// reportes abiertos, urgente si el motivo es <see cref="EquipmentIssueReason.Unsafe"/>) y, en los urgentes, encola
/// el correo al Owner. <paramref name="OtherOpenIssueIds"/> son los demás reportes abiertos de la misma máquina al
/// momento de reportar (para agrupar el aviso). El nombre de quien reportó lo resuelve Notifications por Identity.
/// </summary>
public sealed record EquipmentIssueReported(
    Guid GymId,
    Guid IssueId,
    Guid ItemId,
    string ItemName,
    EquipmentIssueReason Reason,
    string? Description,
    Guid ReportedByUserId,
    IssueReporterRole ReportedByRole,
    IReadOnlyList<Guid> OtherOpenIssueIds,
    DateTimeOffset ReportedAt) : IDomainEvent;

/// <summary>
/// Un reporte se cerró (E4): <paramref name="Dismissed"/> = descartado (sin aviso al miembro). <paramref name="ItemIssueIds"/>
/// son todos los reportes de la máquina y <paramref name="OpenIssuesLeft"/> cuántos siguen sin cerrar: el aviso del
/// staff se atiende solo cuando ya no queda ninguno.
/// </summary>
public sealed record EquipmentIssueClosed(
    Guid GymId,
    Guid IssueId,
    Guid ItemId,
    string ItemName,
    bool Dismissed,
    Guid? ReportedByUserId,
    IssueReporterRole ReportedByRole,
    Guid ClosedBy,
    int OpenIssuesLeft,
    IReadOnlyList<Guid> ItemIssueIds,
    DateTimeOffset ClosedAt) : IDomainEvent;

/// <summary>
/// Alguien del staff abrió el reporte por primera vez (E4). <paramref name="AllOpenSeen"/>: ya no queda un reporte
/// abierto de la máquina sin ver; entonces Notifications atiende el aviso del staff.
/// </summary>
public sealed record EquipmentIssueSeen(
    Guid GymId,
    Guid IssueId,
    Guid ItemId,
    Guid SeenBy,
    bool AllOpenSeen,
    IReadOnlyList<Guid> ItemIssueIds,
    DateTimeOffset SeenAt) : IDomainEvent;
