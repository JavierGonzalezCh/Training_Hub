using GSM.Application.Classes.Abstractions;
using GSM.Application.Classes.Contracts;
using GSM.Application.Classes.Events;
using GSM.Application.Common.Abstractions;
using GSM.Application.Common.Events;
using GSM.Application.Equipment.Public;
using GSM.Application.Gyms.Public;
using GSM.Application.Identity.Public;
using GSM.Domain.Classes;
using GSM.Domain.Common.Exceptions;
using Microsoft.Extensions.Logging;

namespace GSM.Application.Classes;

/// <summary>
/// C2: resuelve el instructor de una clase. Un entrenador de la lista se valida contra Identity (Owner o
/// permiso <c>Trainer</c>) y su nombre se copia de la cuenta; "Otro" deja <c>InstructorUserId</c> nulo y
/// conserva el texto libre.
/// </summary>
internal static class ClassInstructor
{
    public static async Task<SaveClassRequest> ResolveAsync(
        IIdentityReader identity,
        Guid gymId,
        SaveClassRequest request,
        Guid? currentInstructorUserId,
        CancellationToken cancellationToken)
    {
        if (request.InstructorUserId is not { } userId)
        {
            return request;
        }

        // Quien ya era el instructor no se revalida: si perdió el permiso, la clase se queda como estaba
        // (aparece en "sin entrenador activo") hasta que el staff la reasigne.
        if (userId != currentInstructorUserId
            && !await identity.CanBeTrainerAsync(userId, gymId, cancellationToken))
        {
            throw new ValidationDomainException(
                "Esa persona no puede ser instructor: debe ser el Owner o tener el permiso de entrenador en este gimnasio.");
        }

        var user = await identity.GetUserAsync(userId, cancellationToken)
            ?? throw new ValidationDomainException("El entrenador elegido no existe.");
        var name = user.FullName.Length > 120 ? user.FullName[..120] : user.FullName;
        return request with { InstructorName = name };
    }

    /// <summary>Ids de las cuentas que hoy pueden ser entrenador en el gym.</summary>
    public static async Task<HashSet<Guid>> ActiveTrainerIdsAsync(
        IIdentityReader identity, Guid gymId, CancellationToken cancellationToken)
        => (await identity.ListTrainersAsync(gymId, cancellationToken)).Select(t => t.UserId).ToHashSet();

    public static bool HasNoActiveInstructor(GymClass c, HashSet<Guid> activeTrainers)
        => c.IsActive && c.InstructorUserId is { } id && !activeTrainers.Contains(id);
}

public sealed class ListClassesHandler(
    ICurrentUserService currentUser, IClassRepository classes, IIdentityReader identity)
{
    public async Task<IReadOnlyList<ClassListItem>> HandleAsync(
        bool includeInactive, CancellationToken cancellationToken)
    {
        var gymId = currentUser.RequireGymId();

        var list = await classes.ListAsync(new ClassFilter(includeInactive), cancellationToken);
        var trainers = list.Any(c => c.InstructorUserId is not null)
            ? await ClassInstructor.ActiveTrainerIdsAsync(identity, gymId, cancellationToken)
            : [];
        return list.Select(c => ClassMapper.ToListItem(c, ClassInstructor.HasNoActiveInstructor(c, trainers))).ToList();
    }
}

public sealed class GetClassHandler(
    ICurrentUserService currentUser, IClassRepository classes, IIdentityReader identity,
    IClassEquipmentRepository equipmentLinks)
{
    public async Task<ClassResponse> HandleAsync(Guid classId, CancellationToken cancellationToken)
    {
        var gymId = currentUser.RequireGymId();

        var gymClass = await classes.GetAsync(classId, cancellationToken)
            ?? throw new NotFoundException("Clase no encontrada.");

        var trainers = gymClass.InstructorUserId is not null
            ? await ClassInstructor.ActiveTrainerIdsAsync(identity, gymId, cancellationToken)
            : [];
        var machineIds = (await equipmentLinks.ListForClassAsync(classId, cancellationToken))
            .Select(l => l.EquipmentItemId).ToList();
        return ClassMapper.ToResponse(gymClass, ClassInstructor.HasNoActiveInstructor(gymClass, trainers), machineIds);
    }
}

/// <summary>C2: la lista de entrenadores del gym para el selector de instructor (cualquier staff la ve).</summary>
public sealed class ListClassInstructorsHandler(ICurrentUserService currentUser, IIdentityReader identity)
{
    public async Task<IReadOnlyList<ClassInstructorOption>> HandleAsync(CancellationToken cancellationToken)
    {
        var gymId = currentUser.RequireGymId();
        var trainers = await identity.ListTrainersAsync(gymId, cancellationToken);
        return trainers.Select(t => new ClassInstructorOption(t.UserId, t.FullName, t.IsOwner)).ToList();
    }
}

public sealed class CreateClassHandler(
    ICurrentUserService currentUser,
    IClock clock,
    IGymReader gyms,
    IIdentityReader identity,
    IEquipmentReader equipment,
    IClassRepository classes,
    IClassEquipmentRepository equipmentLinks,
    IUnitOfWork unitOfWork,
    IDomainEventPublisher events,
    ILogger<CreateClassHandler> logger)
{
    public async Task<ClassResponse> HandleAsync(SaveClassRequest request, CancellationToken cancellationToken)
    {
        var gymId = currentUser.RequireGymId();

        request = await ClassInstructor.ResolveAsync(identity, gymId, request, null, cancellationToken);
        var gymClass = GymClass.Create(gymId, clock.Today, ClassMapper.ToDraft(request));

        // C1: la clase solo se agenda dentro del horario del gym.
        await ClassOpeningHours.EnsureDefinitionFitsAsync(gyms, clock, gymId, request, cancellationToken);

        // C6: las máquinas ligadas se validan contra el inventario (Equipment) y exigen cupo.
        var machineIds = await ClassEquipmentLinks.ValidateAsync(
            equipment, gymId, request.EquipmentItemIds, gymClass.Capacity, cancellationToken);

        classes.Add(gymClass);
        ClassEquipmentLinks.Sync(equipmentLinks, gymId, gymClass.Id, [], machineIds);
        await unitOfWork.SaveChangesAsync(cancellationToken);

        logger.LogInformation(
            "Clase {ClassId} creada en el gym {GymId} (recurrente: {IsRecurring})",
            gymClass.Id, gymId, gymClass.IsRecurring);

        // C8: aviso de clase nueva, solo al crear y si el staff no lo desmarcó. Si el manejador falla, la clase queda creada.
        if (request.NotifyMembers)
        {
            await events.PublishAsync(
                new ClassCreated(
                    gymId, gymClass.Id, gymClass.Name, gymClass.InstructorName, gymClass.StartTime,
                    gymClass.IsRecurring, gymClass.DaysOfWeek.ToList(), gymClass.ScheduledDate, currentUser.UserId),
                cancellationToken);
        }

        return ClassMapper.ToResponse(gymClass, equipmentItemIds: machineIds);
    }
}

public sealed class UpdateClassHandler(
    ICurrentUserService currentUser,
    IClock clock,
    IGymReader gyms,
    IIdentityReader identity,
    IEquipmentReader equipment,
    IClassRepository classes,
    IClassSessionRepository sessions,
    IClassEquipmentRepository equipmentLinks,
    IUnitOfWork unitOfWork,
    ILogger<UpdateClassHandler> logger)
{
    public async Task<ClassResponse> HandleAsync(
        Guid classId, SaveClassRequest request, CancellationToken cancellationToken)
    {
        var gymId = currentUser.RequireGymId();

        var gymClass = await classes.GetAsync(classId, cancellationToken)
            ?? throw new NotFoundException("Clase no encontrada.");

        var previousUserId = gymClass.InstructorUserId;
        var previousName = gymClass.InstructorName;
        request = await ClassInstructor.ResolveAsync(identity, gymId, request, previousUserId, cancellationToken);
        gymClass.Update(ClassMapper.ToDraft(request));

        // C1: editar también exige que la clase quepa en el horario del gym.
        await ClassOpeningHours.EnsureDefinitionFitsAsync(gyms, clock, gymId, request, cancellationToken);

        // C2: reasignar el instructor alcanza también a las sesiones futuras ya materializadas (el roster y
        // "Mis clases" salen de ellas); las pasadas conservan quién la dio.
        if (previousUserId != gymClass.InstructorUserId || previousName != gymClass.InstructorName)
        {
            foreach (var session in await sessions.ListFromAsync(classId, clock.Today, cancellationToken))
            {
                if (!session.IsCancelled)
                {
                    session.ReassignInstructor(gymClass.InstructorUserId, gymClass.InstructorName);
                }
            }
        }

        // C6: sin lista en la petición las ligas no se tocan; con lista (aunque vacía) quedan exactamente esas. Si la clase
        // pasa a entrada libre con máquinas ligadas, se rechaza (quitar las ligas es explícito).
        var existingLinks = await equipmentLinks.ListForClassAsync(classId, cancellationToken);
        var desired = request.EquipmentItemIds is null
            ? existingLinks.Select(l => l.EquipmentItemId).ToList()
            : await ClassEquipmentLinks.ValidateAsync(
                equipment, gymId, request.EquipmentItemIds, gymClass.Capacity, cancellationToken);
        if (request.EquipmentItemIds is null && desired.Count > 0 && gymClass.Capacity is null)
        {
            throw new ValidationDomainException(
                "La clase está ligada a máquinas: quítalas antes de pasarla a entrada libre.");
        }

        ClassEquipmentLinks.Sync(equipmentLinks, gymId, classId, existingLinks, desired);
        await unitOfWork.SaveChangesAsync(cancellationToken);

        logger.LogInformation("Clase {ClassId} actualizada", gymClass.Id);
        return ClassMapper.ToResponse(gymClass, equipmentItemIds: desired);
    }
}

/// <summary>
/// Da de baja una clase. Sus sesiones futuras ya materializadas con reservas activas se cancelan ("Clase dada de
/// baja") y se publica <see cref="ClassSessionCancelled"/> por cada una (N5-6): el mismo aviso de una sesión
/// cancelada, y Notifications retira los recordatorios. Las ocurrencias sin materializar no tienen reservas que
/// cancelar: dejan de proyectarse solas.
/// </summary>
public sealed class DeactivateClassHandler(
    ICurrentUserService currentUser,
    IClock clock,
    IClassRepository classes,
    IClassSessionRepository sessions,
    IClassBookingRepository bookings,
    IUnitOfWork unitOfWork,
    IDomainEventPublisher events,
    ILogger<DeactivateClassHandler> logger)
{
    public const string CancellationReason = "Clase dada de baja";

    public async Task HandleAsync(Guid classId, CancellationToken cancellationToken)
    {
        var gymId = currentUser.RequireGymId();

        var gymClass = await classes.GetAsync(classId, cancellationToken)
            ?? throw new NotFoundException("Clase no encontrada.");

        if (!gymClass.IsActive)
        {
            return;
        }

        var today = clock.Today;
        gymClass.Deactivate(today);

        var cancelled = new List<(DateOnly Date, List<Guid> Affected)>();
        foreach (var session in await sessions.ListFromAsync(classId, today, cancellationToken))
        {
            if (session.IsCancelled)
            {
                continue;
            }

            var active = (await bookings.ListForSessionAsync(session.Id, cancellationToken))
                .Where(b => b.IsActive).ToList();
            if (active.Count == 0)
            {
                continue;
            }

            session.Cancel(CancellationReason);
            foreach (var booking in active)
            {
                booking.CancelBySessionCancellation(clock.UtcNow);
            }

            cancelled.Add((session.SessionDate, active.Select(b => b.MemberId).ToList()));
        }

        await unitOfWork.SaveChangesAsync(cancellationToken);
        logger.LogInformation(
            "Clase {ClassId} dada de baja el {Date}: {Sessions} sesiones futuras canceladas",
            gymClass.Id, gymClass.DeactivatedOn, cancelled.Count);

        foreach (var (date, affected) in cancelled)
        {
            await events.PublishAsync(new ClassSessionCancelled(classId, gymId, date, affected), cancellationToken);
        }
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
