namespace GSM.Application.Memberships.Contracts;

public sealed record CreatePlanRequest(string Name, string? Description, decimal Price, int DurationDays);

public sealed record UpdatePlanRequest(string Name, string? Description, decimal Price, int DurationDays);

public sealed record PlanResponse(
    Guid Id,
    string Name,
    string? Description,
    decimal Price,
    int DurationDays,
    bool IsActive,
    DateTimeOffset CreatedAt);

public sealed record AssignMembershipRequest(Guid PlanId, DateOnly? StartDate);

public sealed record MembershipResponse(
    Guid Id,
    Guid MemberId,
    Guid PlanId,
    string PlanName,
    string Status,
    DateOnly StartDate,
    DateOnly EndDate,
    DateTimeOffset CreatedAt,
    DateOnly? PausedOn,
    string? CancellationReason);

public sealed record CancelMembershipRequest(string Reason);

public sealed record ExpiringMembershipItem(
    Guid MembershipId,
    Guid MemberId,
    string MemberName,
    string PlanName,
    DateOnly EndDate,
    int DaysLeft);
