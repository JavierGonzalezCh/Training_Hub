namespace GSM.Application.Notifications.Contracts;

/// <summary>Un aviso de la bandeja del miembro.</summary>
public sealed record NotificationItem(
    Guid Id,
    string Type,
    string Title,
    string Body,
    string? Link,
    DateTimeOffset CreatedAt,
    DateTimeOffset? ReadAt);

/// <summary>Un aviso de la bandeja del staff (campana del panel).</summary>
/// <param name="RequiresAction">De acción: se atiende para todos. Si no, es informativo y cada persona lo lee.</param>
/// <param name="ResolvedByName">Quién lo atendió; nulo si aún está abierto o no se pudo resolver el nombre.</param>
public sealed record StaffNotificationItem(
    Guid Id,
    string Type,
    string Title,
    string Body,
    string? Link,
    string Priority,
    bool RequiresAction,
    DateTimeOffset CreatedAt,
    DateTimeOffset? ReadAt,
    DateTimeOffset? ResolvedAt,
    Guid? ResolvedBy,
    string? ResolvedByName);

public sealed record StaffNotificationCount(int Count);
