namespace GSM.Domain.Classes;

/// <summary>
/// Datos de entrada para crear o reemplazar una <see cref="GymClass"/>. El caso de uso valida lo
/// que necesita del contexto (gym); el agregado valida sus propias invariantes de forma.
/// </summary>
public sealed record GymClassDraft(
    string Name,
    string? Description,
    string? InstructorName,
    int? Capacity,
    TimeOnly StartTime,
    int DurationMinutes,
    bool IsRecurring,
    IReadOnlyList<ClassDayOfWeek> DaysOfWeek,
    DateOnly? ScheduledDate,
    DateOnly? EndsOn = null);
