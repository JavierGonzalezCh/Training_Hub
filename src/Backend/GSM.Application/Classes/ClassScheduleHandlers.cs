using GSM.Application.Classes.Abstractions;
using GSM.Application.Classes.Contracts;
using GSM.Application.Classes.Events;
using GSM.Application.Common.Abstractions;
using GSM.Application.Common.Events;
using GSM.Application.Gyms.Public;
using GSM.Application.Members.Public;
using GSM.Domain.Common.Exceptions;
using Microsoft.Extensions.Logging;

namespace GSM.Application.Classes;

/// <summary>Rango máximo que se puede pedir de una vez, para acotar el cálculo de la recurrencia.</summary>
internal static class ScheduleWindow
{
    public const int MaxDays = 92;

    public static void Validate(DateOnly from, DateOnly to)
    {
        if (to < from)
        {
            throw new ValidationDomainException("El rango de fechas es inválido: 'to' es anterior a 'from'.");
        }

        if (to.DayNumber - from.DayNumber > MaxDays)
        {
            throw new ValidationDomainException($"El rango no puede exceder {MaxDays} días.");
        }
    }
}

/// <summary>Horario proyectado del gym (staff): ocurrencias calculadas + sesiones materializadas.</summary>
public sealed class GetScheduleHandler(
    ICurrentUserService currentUser,
    IClassRepository classes,
    IClassSessionRepository sessions)
{
    public async Task<ScheduleResponse> HandleAsync(
        DateOnly from, DateOnly to, CancellationToken cancellationToken)
    {
        _ = currentUser.RequireGymId();
        ScheduleWindow.Validate(from, to);

        var defs = await classes.ListAsync(new ClassFilter(IncludeInactive: true), cancellationToken);
        var mat = await sessions.ListInRangeAsync(from, to, cancellationToken);

        return new ScheduleResponse(from, to, ClassSchedule.Project(defs, mat, from, to));
    }
}

/// <summary>Horario del gym para la app del socio. 403 si <c>show_classes = false</c>.</summary>
public sealed class GetMyClassesHandler(
    ICurrentUserService currentUser,
    IGymReader gyms,
    IMemberReader members,
    IClassRepository classes,
    IClassSessionRepository sessions)
{
    public async Task<ScheduleResponse> HandleAsync(
        DateOnly from, DateOnly to, CancellationToken cancellationToken)
    {
        var gymId = currentUser.RequireGymId();
        await ClassFeatureGate.EnsureMemberModuleEnabledAsync(gyms, gymId, cancellationToken);
        ScheduleWindow.Validate(from, to);

        _ = await members.ResolveByUserIdAsync(currentUser.UserId ?? Guid.Empty, cancellationToken)
            ?? throw new ForbiddenDomainException("Tu cuenta no está vinculada a un miembro.");

        var defs = await classes.ListAsync(new ClassFilter(IncludeInactive: true), cancellationToken);
        var mat = await sessions.ListInRangeAsync(from, to, cancellationToken);

        // El estado de las reservas del socio se añade en la sub-etapa 10.3.
        return new ScheduleResponse(from, to, ClassSchedule.Project(defs, mat, from, to));
    }
}

/// <summary>
/// Cancela una ocurrencia puntual (festivo, instructor enfermo): materializa la sesión con
/// <c>status = cancelled</c> sin tocar el resto de la serie. Las reservas afectadas pasan a
/// <c>cancelled</c> en la sub-etapa 10.3.
/// </summary>
public sealed class CancelSessionHandler(
    ICurrentUserService currentUser,
    IClassRepository classes,
    IClassSessionRepository sessions,
    IUnitOfWork unitOfWork,
    IDomainEventPublisher events,
    ILogger<CancelSessionHandler> logger)
{
    public async Task<ScheduleEntry> HandleAsync(
        Guid classId, DateOnly date, string? reason, CancellationToken cancellationToken)
    {
        var gymId = currentUser.RequireGymId();

        var gymClass = await classes.GetAsync(classId, cancellationToken)
            ?? throw new NotFoundException("Clase no encontrada.");

        var isOccurrence = ClassSchedule.OccurrencesOf(gymClass, date, date).Any();
        var existing = await sessions.GetAsync(classId, date, cancellationToken);

        if (existing is null && !isOccurrence)
        {
            throw new ValidationDomainException("Esa fecha no corresponde a una ocurrencia de la clase.");
        }

        var session = existing
            ?? await ClassSessionMaterializer.GetOrCreateAsync(sessions, unitOfWork, gymClass, date, cancellationToken);

        if (!session.IsCancelled)
        {
            session.Cancel(reason);
            await unitOfWork.SaveChangesAsync(cancellationToken);
            logger.LogInformation(
                "Sesión de la clase {ClassId} del {Date} cancelada", classId, date);
        }

        await events.PublishAsync(
            new ClassSessionCancelled(classId, gymId, date, AffectedMemberIds: []), cancellationToken);

        return new ScheduleEntry(
            gymClass.Id, gymClass.Name, gymClass.Description, session.InstructorName, session.Capacity,
            session.SessionDate, session.StartTime, session.DurationMinutes,
            session.Status.ToString(), session.CancellationReason, IsMaterialized: true, session.Id);
    }
}
