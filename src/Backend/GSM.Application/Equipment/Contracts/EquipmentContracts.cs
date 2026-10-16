using GSM.Domain.Equipment;

namespace GSM.Application.Equipment.Contracts;

/// <summary>Modelo del catálogo del sistema. <c>Type</c> sale como texto (nombre del enum).</summary>
public sealed record EquipmentModelResponse(
    Guid Id,
    string Name,
    string Type,
    string? ImageUrl,
    string? Description,
    string? DefaultInstructions,
    bool TrackedPerUnit);

/// <summary>
/// Una máquina del inventario. <c>Type</c> y <c>Status</c> salen como texto. <c>ModelImageUrl</c> es la
/// imagen de referencia del modelo (si salió del catálogo); <c>HasOwnPhoto</c> dice si el gym subió la suya.
/// </summary>
public sealed record EquipmentItemResponse(
    Guid Id,
    Guid? ModelId,
    string Name,
    string Type,
    string? PhotoUrl,
    string? ModelImageUrl,
    bool HasOwnPhoto,
    string? Zone,
    string? Brand,
    string? Model,
    string? Instructions,
    int Quantity,
    string Status,
    DateOnly? ExpectedBackOn,
    DateOnly? AcquiredOn,
    string? Notes,
    bool IsActive,
    bool IsOverdue);

/// <summary>Crea (máquina propia) o edita una máquina. El estado no se cambia aquí (E2).</summary>
public sealed record SaveEquipmentRequest(
    string Name,
    EquipmentType Type,
    string? Zone,
    string? Brand,
    string? Model,
    string? Instructions,
    int Quantity,
    DateOnly? AcquiredOn,
    string? Notes);

/// <summary>
/// Cambia el estado de una máquina. <c>Reason</c> es obligatorio al pasar a mantenimiento o fuera de
/// servicio. <c>ExpectedBackOn</c> (opcional) solo aplica fuera de disponible y no puede ser pasada.
/// </summary>
public sealed record ChangeEquipmentStatusRequest(EquipmentStatus Status, string? Reason, DateOnly? ExpectedBackOn);

/// <summary>Una fila del historial de estado. <c>ChangedByName</c> sale de Identity (null si la cuenta ya no existe).</summary>
public sealed record EquipmentStatusChangeResponse(
    Guid Id,
    string FromStatus,
    string ToStatus,
    string? Reason,
    DateOnly? ExpectedBackOn,
    Guid? ChangedByUserId,
    string? ChangedByName,
    DateTimeOffset ChangedAt);

/// <summary>Un modelo del catálogo que el gym tiene, con cuántos y en qué zona.</summary>
public sealed record EquipmentFromModelItem(Guid ModelId, int Quantity, string? Zone);

public sealed record AddEquipmentFromModelsRequest(IReadOnlyList<EquipmentFromModelItem> Items);

// ---- E4: reportes de falla ----

/// <summary>Reportar una falla (miembro o staff). <c>Description</c> es obligatoria si <c>Reason</c> es «otro».</summary>
public sealed record ReportEquipmentIssueRequest(EquipmentIssueReason Reason, string? Description);

/// <summary>
/// Cambia el estado de un reporte: <c>InProgress</c>, o <c>Resolved</c>/<c>Dismissed</c> con <c>Resolution</c> obligatoria.
/// </summary>
public sealed record UpdateEquipmentIssueRequest(EquipmentIssueStatus Status, string? Resolution);

/// <summary>
/// Un reporte, para el staff. <c>Reason</c>, <c>Status</c> y <c>ReportedByRole</c> salen como texto. <c>IsSeen</c>
/// = alguien del staff ya lo abrió. <c>ReportedByName</c> es nulo si la cuenta se anonimizó.
/// </summary>
public sealed record EquipmentIssueResponse(
    Guid Id,
    Guid ItemId,
    string ItemName,
    string? ItemZone,
    string? ItemPhotoUrl,
    string Reason,
    string? Description,
    string? PhotoUrl,
    string Status,
    bool IsUrgent,
    bool IsSeen,
    string ReportedByRole,
    string? ReportedByName,
    DateTimeOffset CreatedAt,
    DateTimeOffset? ResolvedAt,
    string? ResolvedByName,
    string? Resolution);

/// <summary>Un reporte propio del miembro. <c>Resolution</c> solo sale si se resolvió (un descarte no se explica al miembro).</summary>
public sealed record MyEquipmentIssueResponse(
    Guid Id,
    Guid ItemId,
    string ItemName,
    string Reason,
    string? Description,
    string Status,
    DateTimeOffset CreatedAt,
    DateTimeOffset? ResolvedAt,
    string? Resolution);

/// <summary>
/// Una máquina vista por el miembro: foto (propia o del modelo), zona, instrucciones y estado, más si puede reportarla
/// (<c>CanReport</c> = el flag del gym está encendido) y su reporte sin cerrar, si lo tiene.
/// </summary>
public sealed record MyEquipmentResponse(
    Guid Id,
    string Name,
    string Type,
    string? PhotoUrl,
    string? Zone,
    string? Instructions,
    string Status,
    DateOnly? ExpectedBackOn,
    bool CanReport,
    MyEquipmentIssueResponse? OpenIssue);

/// <summary>Tarjeta del dashboard: máquinas fuera de servicio o en mantenimiento y reportes abiertos.</summary>
public sealed record EquipmentSummaryResponse(int OutOfService, int InMaintenance, int OpenIssues, int UrgentIssues);

/// <summary>S4: modelo del catálogo tal como lo ve el SuperAdmin; incluye los inactivos. <c>Type</c> sale como texto.</summary>
public sealed record AdminEquipmentModelResponse(
    Guid Id,
    string Name,
    string Type,
    string? ImageUrl,
    string? Description,
    string? DefaultInstructions,
    bool TrackedPerUnit,
    bool IsActive);

/// <summary>S4: alta y edición de un modelo del catálogo. La imagen se sube aparte.</summary>
public sealed record SaveEquipmentModelRequest(
    string Name,
    EquipmentType Type,
    string? Description,
    string? DefaultInstructions,
    bool TrackedPerUnit);
