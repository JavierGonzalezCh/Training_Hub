using GSM.Application.Classes.Abstractions;
using GSM.Application.Classes.Contracts;
using GSM.Application.Common.Abstractions;
using GSM.Application.Gyms.Public;
using GSM.Domain.Classes;
using GSM.Domain.Common.Exceptions;
using Microsoft.Extensions.Logging;

namespace GSM.Application.Classes;

public sealed class ListClassesHandler(ICurrentUserService currentUser, IClassRepository classes)
{
    public async Task<IReadOnlyList<ClassListItem>> HandleAsync(
        bool includeInactive, CancellationToken cancellationToken)
    {
        _ = currentUser.RequireGymId();

        var list = await classes.ListAsync(new ClassFilter(includeInactive), cancellationToken);
        return list.Select(ClassMapper.ToListItem).ToList();
    }
}

public sealed class GetClassHandler(ICurrentUserService currentUser, IClassRepository classes)
{
    public async Task<ClassResponse> HandleAsync(Guid classId, CancellationToken cancellationToken)
    {
        _ = currentUser.RequireGymId();

        var gymClass = await classes.GetAsync(classId, cancellationToken)
            ?? throw new NotFoundException("Clase no encontrada.");

        return ClassMapper.ToResponse(gymClass);
    }
}

public sealed class CreateClassHandler(
    ICurrentUserService currentUser,
    IClock clock,
    IGymReader gyms,
    IClassRepository classes,
    IUnitOfWork unitOfWork,
    ILogger<CreateClassHandler> logger)
{
    public async Task<ClassResponse> HandleAsync(SaveClassRequest request, CancellationToken cancellationToken)
    {
        var gymId = currentUser.RequireGymId();

        var gymClass = GymClass.Create(gymId, clock.Today, ClassMapper.ToDraft(request));

        // C1: la clase solo se agenda dentro del horario del gym.
        await ClassOpeningHours.EnsureDefinitionFitsAsync(gyms, clock, gymId, request, cancellationToken);

        classes.Add(gymClass);
        await unitOfWork.SaveChangesAsync(cancellationToken);

        logger.LogInformation(
            "Clase {ClassId} creada en el gym {GymId} (recurrente: {IsRecurring})",
            gymClass.Id, gymId, gymClass.IsRecurring);

        return ClassMapper.ToResponse(gymClass);
    }
}

public sealed class UpdateClassHandler(
    ICurrentUserService currentUser,
    IClock clock,
    IGymReader gyms,
    IClassRepository classes,
    IUnitOfWork unitOfWork,
    ILogger<UpdateClassHandler> logger)
{
    public async Task<ClassResponse> HandleAsync(
        Guid classId, SaveClassRequest request, CancellationToken cancellationToken)
    {
        var gymId = currentUser.RequireGymId();

        var gymClass = await classes.GetAsync(classId, cancellationToken)
            ?? throw new NotFoundException("Clase no encontrada.");

        gymClass.Update(ClassMapper.ToDraft(request));

        // C1: editar también exige que la clase quepa en el horario del gym.
        await ClassOpeningHours.EnsureDefinitionFitsAsync(gyms, clock, gymId, request, cancellationToken);
        await unitOfWork.SaveChangesAsync(cancellationToken);

        logger.LogInformation("Clase {ClassId} actualizada", gymClass.Id);
        return ClassMapper.ToResponse(gymClass);
    }
}

public sealed class DeactivateClassHandler(
    ICurrentUserService currentUser,
    IClock clock,
    IClassRepository classes,
    IUnitOfWork unitOfWork,
    ILogger<DeactivateClassHandler> logger)
{
    public async Task HandleAsync(Guid classId, CancellationToken cancellationToken)
    {
        _ = currentUser.RequireGymId();

        var gymClass = await classes.GetAsync(classId, cancellationToken)
            ?? throw new NotFoundException("Clase no encontrada.");

        if (!gymClass.IsActive)
        {
            return;
        }

        gymClass.Deactivate(clock.Today);
        await unitOfWork.SaveChangesAsync(cancellationToken);

        logger.LogInformation("Clase {ClassId} dada de baja el {Date}", gymClass.Id, gymClass.DeactivatedOn);
    }
}

/// <summary>
/// Clases activas que dejaron de caber en el horario del gym (el gym cambió su horario y el cambio no se
/// bloquea). El panel las lista para que el staff las mueva; mientras tanto sus ocurrencias fuera de
/// horario no se pueden reservar. Las puntuales ya pasadas no cuentan.
/// </summary>
public sealed class ListClassesOutsideHoursHandler(
    ICurrentUserService currentUser,
    IClock clock,
    IGymReader gyms,
    IClassRepository classes)
{
    public async Task<IReadOnlyList<ClassOutsideHoursItem>> HandleAsync(CancellationToken cancellationToken)
    {
        var gymId = currentUser.RequireGymId();
        var today = clock.Today;

        var active = await classes.ListAsync(new ClassFilter(IncludeInactive: false), cancellationToken);
        var oneOffDates = active.Where(c => !c.IsRecurring && c.ScheduledDate is { } d && d >= today)
            .Select(c => c.ScheduledDate!.Value).ToList();

        var to = oneOffDates.Count == 0 ? today : oneOffDates.Max();
        var schedule = await gyms.GetOpeningScheduleAsync(gymId, today, to, cancellationToken);

        var items = new List<ClassOutsideHoursItem>();
        foreach (var c in active.OrderBy(c => c.Name))
        {
            if (c.IsRecurring)
            {
                // C5: una serie que ya terminó no se agenda más; no hay nada que mover.
                if (c.EndsOn is { } endsOn && endsOn < today)
                {
                    continue;
                }

                var outside = ClassOpeningHours.WeekdaysOutside(schedule, c.DaysOfWeek, c.StartTime, c.DurationMinutes);
                if (outside.Count > 0)
                {
                    items.Add(new ClassOutsideHoursItem(
                        c.Id, c.Name, c.StartTime, c.DurationMinutes, true,
                        outside.Select(d => d.ToString()).ToList(), null));
                }
            }
            else if (c.ScheduledDate is { } date && date >= today
                && !ClassOpeningHours.OccurrenceFits(schedule, date, c.StartTime, c.DurationMinutes))
            {
                items.Add(new ClassOutsideHoursItem(
                    c.Id, c.Name, c.StartTime, c.DurationMinutes, false, [], date));
            }
        }

        return items;
    }
}
