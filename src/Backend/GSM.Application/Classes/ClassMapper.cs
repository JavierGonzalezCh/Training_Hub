using GSM.Application.Classes.Contracts;
using GSM.Domain.Classes;

namespace GSM.Application.Classes;

/// <summary>Mapeo manual entidad → DTO, en línea con <c>RoutineMapper</c>.</summary>
public static class ClassMapper
{
    public static ClassResponse ToResponse(GymClass c)
        => new(
            c.Id,
            c.Name,
            c.Description,
            c.InstructorName,
            c.Capacity,
            c.StartTime,
            c.DurationMinutes,
            c.IsRecurring,
            c.DaysOfWeek.ToList(),
            c.ScheduledDate,
            c.StartsOn,
            c.IsActive,
            c.DeactivatedOn,
            c.EndsOn);

    public static ClassListItem ToListItem(GymClass c)
        => new(
            c.Id,
            c.Name,
            c.InstructorName,
            c.Capacity,
            c.StartTime,
            c.DurationMinutes,
            c.IsRecurring,
            c.DaysOfWeek.ToList(),
            c.ScheduledDate,
            c.IsActive,
            c.EndsOn);

    public static GymClassDraft ToDraft(SaveClassRequest r)
        => new(
            r.Name,
            r.Description,
            r.InstructorName,
            r.Capacity,
            r.StartTime,
            r.DurationMinutes,
            r.IsRecurring,
            r.DaysOfWeek ?? [],
            r.ScheduledDate,
            r.EndsOn);
}
