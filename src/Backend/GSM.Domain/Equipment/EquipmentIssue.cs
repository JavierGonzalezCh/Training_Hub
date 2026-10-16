using GSM.Domain.Common;
using GSM.Domain.Common.Exceptions;

namespace GSM.Domain.Equipment;

/// <summary>
/// Reporte de falla de una máquina (Equipment §3.5). Un reporte no cambia el estado de la máquina: el staff lo
/// revisa y decide. Un miembro tiene como máximo un reporte sin cerrar por máquina (lo hace cumplir el caso de
/// uso y un índice único parcial).
/// </summary>
public sealed class EquipmentIssue : BaseEntity, ITenantEntity
{
    public const int DescriptionMaxLength = 1000;
    public const int ResolutionMaxLength = 1000;

    private EquipmentIssue()
    {
    }

    public Guid GymId { get; private set; }

    public Guid ItemId { get; private set; }

    /// <summary>Cuenta que reportó. <c>null</c> si el miembro se anonimizó (el reporte se conserva: es el historial de la máquina).</summary>
    public Guid? ReportedByUserId { get; private set; }

    public IssueReporterRole ReportedByRole { get; private set; }

    public EquipmentIssueReason Reason { get; private set; }

    /// <summary>Obligatoria si el motivo es <see cref="EquipmentIssueReason.Other"/>.</summary>
    public string? Description { get; private set; }

    public string? PhotoUrl { get; private set; }

    public EquipmentIssueStatus Status { get; private set; } = EquipmentIssueStatus.Open;

    /// <summary>Primera vez que alguien del staff abrió el reporte. Mientras sea <c>null</c> sigue "sin ver".</summary>
    public DateTimeOffset? SeenAt { get; private set; }

    public Guid? SeenByUserId { get; private set; }

    public DateTimeOffset? ResolvedAt { get; private set; }

    public Guid? ResolvedByUserId { get; private set; }

    /// <summary>Obligatoria al cerrar (resuelto o descartado).</summary>
    public string? Resolution { get; private set; }

    public bool IsUrgent => Reason == EquipmentIssueReason.Unsafe;

    public bool IsClosed => Status is EquipmentIssueStatus.Resolved or EquipmentIssueStatus.Dismissed;

    public static EquipmentIssue Report(
        Guid gymId,
        Guid itemId,
        Guid reportedByUserId,
        IssueReporterRole role,
        EquipmentIssueReason reason,
        string? description)
    {
        if (gymId == Guid.Empty || itemId == Guid.Empty || reportedByUserId == Guid.Empty)
        {
            throw new ValidationDomainException("El reporte necesita gym, equipo y quien lo hace.");
        }

        if (!Enum.IsDefined(reason) || !Enum.IsDefined(role))
        {
            throw new ValidationDomainException("Motivo de reporte no válido.");
        }

        var text = EquipmentText.Optional(description, DescriptionMaxLength);
        if (reason == EquipmentIssueReason.Other && text is null)
        {
            throw new ValidationDomainException(
                "Cuéntanos qué pasa con el equipo (el motivo \"otro\" necesita una descripción).");
        }

        return new EquipmentIssue
        {
            GymId = gymId,
            ItemId = itemId,
            ReportedByUserId = reportedByUserId,
            ReportedByRole = role,
            Reason = reason,
            Description = text,
        };
    }

    public void SetPhoto(string? photoUrl) => PhotoUrl = EquipmentText.Optional(photoUrl, 2048);

    /// <summary>Marca el reporte como visto por el staff. Idempotente: el primero que lo abre gana.</summary>
    /// <returns><c>true</c> si esta llamada fue la primera.</returns>
    public bool MarkSeen(Guid userId, DateTimeOffset now)
    {
        if (SeenAt is not null)
        {
            return false;
        }

        SeenAt = now;
        SeenByUserId = userId;
        return true;
    }

    /// <summary>
    /// Cambia el estado del reporte. <c>Open → InProgress</c>; <c>Open/InProgress → Resolved/Dismissed</c> (con
    /// resolución obligatoria). Repetir el estado o tocar un reporte cerrado es un choque (409).
    /// </summary>
    public void ChangeStatus(EquipmentIssueStatus newStatus, string? resolution, Guid userId, DateTimeOffset now)
    {
        if (!Enum.IsDefined(newStatus))
        {
            throw new ValidationDomainException("Estado de reporte no válido.");
        }

        if (IsClosed)
        {
            throw new ConflictException("El reporte ya está cerrado.");
        }

        if (newStatus == Status)
        {
            throw new ConflictException("El reporte ya está en ese estado.");
        }

        if (newStatus == EquipmentIssueStatus.Open)
        {
            throw new ConflictException("Un reporte en revisión no vuelve a abierto.");
        }

        if (newStatus == EquipmentIssueStatus.InProgress)
        {
            Status = newStatus;
            MarkSeen(userId, now);
            return;
        }

        var clean = EquipmentText.Optional(resolution, ResolutionMaxLength);
        if (clean is null)
        {
            throw new ValidationDomainException("Indica cómo se resolvió (o por qué se descarta) el reporte.");
        }

        Status = newStatus;
        Resolution = clean;
        ResolvedAt = now;
        ResolvedByUserId = userId;
        MarkSeen(userId, now);
    }
}
