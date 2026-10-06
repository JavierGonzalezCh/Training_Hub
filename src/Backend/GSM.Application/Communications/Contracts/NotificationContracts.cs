namespace GSM.Application.Communications.Contracts;

public sealed record NotificationItem(
    Guid Id,
    string Type,
    string Title,
    string Body,
    string? Link,
    DateTimeOffset CreatedAt,
    DateTimeOffset? ReadAt);
