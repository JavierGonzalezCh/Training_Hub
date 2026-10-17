using GSM.Application.Classes.Abstractions;
using GSM.Application.Classes.Contracts;
using GSM.Application.Classes.Events;
using GSM.Application.Common.Abstractions;
using GSM.Application.Common.Events;
using GSM.Application.Gyms.Public;
using GSM.Application.Members.Public;
using GSM.Domain.Classes;
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

/// <summary>
/// Horario proyectado del gym (staff): ocurrencias calculadas + sesiones materializadas. Las que caen
/// fuera del horario del gym salen marcadas (<c>OutsideOpeningHours</c>, "Gym cerrado"). Una sesión
/// futura fuera de horario que ya tenía reservas se trata como cancelada: ver <see cref="ClosedSessionCanceller"/>.
/// </summary>
public sealed class GetScheduleHandler(
    ICurrentUserService currentUser,
    IClock clock,
    IGymReader gyms,
    IClassRepository classes,
    IClassSessionRepository sessions,
    ClosedSessionCanceller closedSessions,
    ClassCapacityService capacityService)
{
    public async Task<ScheduleResponse> HandleAsync(
        DateOnly from, DateOnly to, CancellationToken cancellationToken)
    {
        var gymId = currentUser.RequireGymId();
        ScheduleWindow.Validate(from, to);

        var defs = await classes.ListAsync(new ClassFilter(IncludeInactive: true), cancellationToken);
        var mat = await sessions.ListInRangeAsync(from, to, cancellationToken);
        var schedule = await gyms.GetOpeningScheduleAsync(gymId, from, to, cancellationToken);

        var entries = ClassSchedule.Project(defs, mat, from, to);

        if (await closedSessions.CancelAsync(gymId, mat, schedule, clock.Today, cancellationToken))
        {
            mat = await sessions.ListInRangeAsync(from, to, cancellationToken);
            entries = ClassSchedule.Project(defs, mat, from, to);
        }

        var annotated = await capacityService.EnrichAsync(gymId, ClassOpeningHours.Annotate(entries, schedule), cancellationToken);
        return new ScheduleResponse(from, to, annotated);
    }
}

/// <summary>
/// C2: "Mis clases" del entrenador: sus ocurrencias (las de las clases donde es el instructor de la lista) en
/// el rango, con el mismo formato del horario del staff. Un instructor "Otro" no tiene cuenta y no aparece.
/// </summary>
public sealed class GetMyInstructorClassesHandler(
    ICurrentUserService currentUser,
    IGymReader gyms,
    IClassRepository classes,
    IClassSessionRepository sessions,
    ClassCapacityService capacityService)
{
    public async Task<ScheduleResponse> HandleAsync(
        DateOnly from, DateOnly to, CancellationToken cancellationToken)
    {
        var gymId = currentUser.RequireGymId();
        var userId = currentUser.UserId ?? throw new ForbiddenDomainException("Sesión sin usuario.");
        ScheduleWindow.Validate(from, to);

        var defs = await classes.ListAsync(new ClassFilter(IncludeInactive: true), cancellationToken);
        var mat = await sessions.ListInRangeAsync(from, to, cancellationToken);
        var schedule = await gyms.GetOpeningScheduleAsync(gymId, from, to, cancellationToken);

        var mine = ClassSchedule.Project(defs, mat, from, to)
            .Where(e => e.InstructorUserId == userId)
            .ToList();

        var annotated = await capacityService.EnrichAsync(gymId, ClassOpeningHours.Annotate(mine, schedule), cancellationToken);
        return new ScheduleResponse(from, to, annotated);
    }
}

/// <summary>Horario del gym para la app del miembro. 403 si <c>show_classes = false</c>.</summary>
public sealed class GetMyClassesHandler(
    ICurrentUserService currentUser,
    IGymReader gyms,
    IMemberReader members,
    IClassRepository classes,
    IClassSessionRepository sessions,
    IClassBookingRepository bookings,
    IWaitlistRepository waitlist,
    WaitlistService waitlistService,
    ClassCapacityService capacityService)
{
    public async Task<ScheduleResponse> HandleAsync(
        DateOnly from, DateOnly to, CancellationToken cancellationToken)
    {
        var gymId = currentUser.RequireGymId();
        await ClassFeatureGate.EnsureMemberModuleEnabledAsync(gyms, gymId, cancellationToken);
        ScheduleWindow.Validate(from, to);

        var self = await members.ResolveByUserIdAsync(currentUser.UserId ?? Guid.Empty, cancellationToken)
            ?? throw new ForbiddenDomainException("Tu cuenta no está vinculada a un miembro.");

        var defs = await classes.ListAsync(new ClassFilter(IncludeInactive: true), cancellationToken);
        var mat = await sessions.ListInRangeAsync(from, to, cancellationToken);
        var schedule = await gyms.GetOpeningScheduleAsync(gymId, from, to, cancellationToken);
        var entries = await capacityService.EnrichAsync(
            gymId, ClassOpeningHours.Annotate(ClassSchedule.Project(defs, mat, from, to), schedule), cancellationToken);

        var myBookings = await bookings.ListForMemberAsync(self.Id, from, to, cancellationToken);
        var byOccurrence = myBookings.ToDictionary(
            b => (b.Class.Id, b.Session.SessionDate), b => b.Booking.Status.ToString());

        // C4: mirar el horario pone al día las listas de espera donde el miembro espera (lazy, sin scheduler).
        foreach (var sessionId in (await waitlist.ListOpenForMemberAsync(self.Id, from, to, cancellationToken))
                     .Select(w => w.Session.Id).Distinct().ToList())
        {
            await waitlistService.ProcessAsync(sessionId, cancellationToken);
        }

        var myWaits = (await waitlist.ListOpenForMemberAsync(self.Id, from, to, cancellationToken))
            .ToDictionary(w => (w.Class.Id, w.Session.SessionDate));

        // Sesiones con cupo ya materializadas: llenas = reservas + cupos apartados >= cupo.
        var capped = entries.Where(e => e.SessionId is not null && e.Capacity is not null).ToList();
        var cappedIds = capped.Select(e => e.SessionId!.Value).ToList();
        var bookedBySession = await bookings.CountActiveBySessionsAsync(cappedIds, cancellationToken);
        var openBySession = (await waitlist.ListOpenForSessionsAsync(cappedIds, cancellationToken))
            .GroupBy(w => w.SessionId)
            .ToDictionary(g => g.Key, g => g.ToList());

        var enriched = entries
            .Select(e =>
            {
                var result = e;
                if (byOccurrence.TryGetValue((e.ClassId, e.SessionDate), out var status))
                {
                    result = result with { MyBookingStatus = status };
                }

                if (e.SessionId is { } sid && e.Capacity is { } cap)
                {
                    var open = openBySession.GetValueOrDefault(sid) ?? [];
                    var booked = bookedBySession.GetValueOrDefault(sid);
                    var offers = open.Count(w => w.Status == WaitlistEntryStatus.Offered);
                    // C6: "llena" se mide contra el cupo efectivo cuando la clase tiene máquinas ligadas.
                    result = result with { IsFull = booked + offers >= (e.EffectiveCapacity ?? cap) };

                    if (myWaits.TryGetValue((e.ClassId, e.SessionDate), out var mine))
                    {
                        result = result with
                        {
                            MyWaitlistStatus = mine.Entry.Status.ToString(),
                            MyWaitlistPosition = WaitlistService.Rank(open, mine.Entry),
                            MyOfferExpiresAt = mine.Entry.OfferExpiresAt,
                        };
                    }
                }

                // C6: sin máquinas disponibles ese día una ocurrencia aún sin reservas ya está llena (la lista de espera la materializa).
                if (e.SessionId is null && e.Capacity is not null && e.EffectiveCapacity == 0)
                {
                    result = result with { IsFull = true };
                }

                return result;
            })
            .ToList();

        return new ScheduleResponse(from, to, enriched);
    }
}

/// <summary>
/// Cancela una ocurrencia puntual (festivo, instructor enfermo): materializa la sesión con
/// <c>status = cancelled</c> sin tocar el resto de la serie, y pasa a <c>cancelled</c> las reservas
/// que tenía.
/// </summary>
public sealed class CancelSessionHandler(
    ICurrentUserService currentUser,
    IClock clock,
    IClassRepository classes,
    IClassSessionRepository sessions,
    IClassBookingRepository bookings,
    IWaitlistRepository waitlist,
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

        var affected = new List<Guid>();
        var waiting = new List<Guid>();

        if (!session.IsCancelled)
        {
            session.Cancel(reason);

            foreach (var booking in await bookings.ListForSessionAsync(session.Id, cancellationToken))
            {
                if (booking.IsActive)
                {
                    booking.CancelBySessionCancellation(clock.UtcNow);
                    affected.Add(booking.MemberId);
                }
            }

            // C4-2: quienes esperaban cupo también reciben el aviso, y la lista se cierra.
            waiting = await WaitlistClosing.CloseAsync(waitlist, session.Id, cancellationToken);

            await unitOfWork.SaveChangesAsync(cancellationToken);
            logger.LogInformation(
                "Sesión de la clase {ClassId} del {Date} cancelada ({Affected} reservas)",
                classId, date, affected.Count);
        }

        await events.PublishAsync(
            new ClassSessionCancelled(classId, gymId, date, affected, waiting), cancellationToken);

        return new ScheduleEntry(
            gymClass.Id, gymClass.Name, gymClass.Description, session.InstructorName, session.Capacity,
            session.SessionDate, session.StartTime, session.DurationMinutes,
            session.Status.ToString(), session.CancellationReason, IsMaterialized: true, session.Id,
            InstructorUserId: session.InstructorUserId);
    }
}

/// <summary>
/// Sin scheduler no hay quién cancele las reservas de una sesión cuando el gym cambia su horario o
/// añade un cierre: se hace de forma perezosa cuando el staff mira el horario. La sesión futura que
/// ya no cabe en el horario y tenía reservas activas pasa a <c>cancelled</c> ("Gym cerrado"), sus
/// reservas a <c>cancelled</c> y se publica <c>ClassSessionCancelled</c>, el mismo aviso de siempre.
/// Idempotente: una sesión ya cancelada no se vuelve a tocar.
/// </summary>
public sealed class ClosedSessionCanceller(
    IClock clock,
    IClassBookingRepository bookings,
    IWaitlistRepository waitlist,
    IUnitOfWork unitOfWork,
    IDomainEventPublisher events,
    ILogger<ClosedSessionCanceller> logger)
{
    public const string Reason = "Gym cerrado";

    /// <returns><c>true</c> si canceló alguna sesión.</returns>
    public async Task<bool> CancelAsync(
        Guid gymId,
        IEnumerable<ClassSession> materialized,
        GSM.Domain.Gyms.OpeningSchedule schedule,
        DateOnly today,
        CancellationToken cancellationToken)
    {
        if (!schedule.HasSchedule)
        {
            return false;
        }

        var cancelled = new List<(ClassSession Session, List<Guid> Affected, List<Guid> Waiting)>();

        foreach (var session in materialized)
        {
            if (session.IsCancelled || session.SessionDate < today
                || ClassOpeningHours.OccurrenceFits(schedule, session.SessionDate, session.StartTime, session.DurationMinutes))
            {
                continue;
            }

            var active = (await bookings.ListForSessionAsync(session.Id, cancellationToken))
                .Where(b => b.IsActive).ToList();
            var openWaitlist = await waitlist.ListOpenForSessionAsync(session.Id, cancellationToken);
            if (active.Count == 0 && openWaitlist.Count == 0)
            {
                continue;
            }

            session.Cancel(Reason);
            foreach (var booking in active)
            {
                booking.CancelBySessionCancellation(clock.UtcNow);
            }

            // C4-2: la lista de espera también recibe el aviso.
            var waiting = await WaitlistClosing.CloseAsync(waitlist, session.Id, cancellationToken);
            cancelled.Add((session, active.Select(b => b.MemberId).ToList(), waiting));
        }

        if (cancelled.Count == 0)
        {
            return false;
        }

        await unitOfWork.SaveChangesAsync(cancellationToken);

        foreach (var (session, affected, waiting) in cancelled)
        {
            logger.LogInformation(
                "Sesión {SessionId} del {Date} cancelada por quedar fuera del horario del gym ({Affected} reservas)",
                session.Id, session.SessionDate, affected.Count);

            await events.PublishAsync(
                new ClassSessionCancelled(session.ClassId, gymId, session.SessionDate, affected, waiting), cancellationToken);
        }

        return true;
    }
}
