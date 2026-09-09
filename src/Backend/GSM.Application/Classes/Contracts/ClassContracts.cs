using GSM.Domain.Classes;

namespace GSM.Application.Classes.Contracts;

/// <summary>
/// Alta o edición de una definición de clase. <c>Capacity = null</c> significa entrada libre
/// (sin reserva). Para una clase recurrente, <c>DaysOfWeek</c> no vacío y <c>ScheduledDate</c> null;
/// para una puntual, al revés.
/// </summary>
public sealed record SaveClassRequest(
    string Name,
    string? Description,
    string? InstructorName,
    int? Capacity,
    TimeOnly StartTime,
    int DurationMinutes,
    bool IsRecurring,
    IReadOnlyList<ClassDayOfWeek>? DaysOfWeek,
    DateOnly? ScheduledDate);

public sealed record ClassResponse(
    Guid Id,
    string Name,
    string? Description,
    string? InstructorName,
    int? Capacity,
    TimeOnly StartTime,
    int DurationMinutes,
    bool IsRecurring,
    IReadOnlyList<ClassDayOfWeek> DaysOfWeek,
    DateOnly? ScheduledDate,
    DateOnly StartsOn,
    bool IsActive,
    DateOnly? DeactivatedOn);

public sealed record ClassListItem(
    Guid Id,
    string Name,
    string? InstructorName,
    int? Capacity,
    TimeOnly StartTime,
    int DurationMinutes,
    bool IsRecurring,
    IReadOnlyList<ClassDayOfWeek> DaysOfWeek,
    DateOnly? ScheduledDate,
    bool IsActive);
