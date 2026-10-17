using GSM.Application.Classes.Abstractions;
using GSM.Application.Classes.Contracts;
using GSM.Application.Classes.Events;
using GSM.Application.Common.Abstractions;
using GSM.Application.Common.Events;
using GSM.Application.Gyms;
using GSM.Application.Gyms.Public;
using GSM.Application.Members.Public;
using GSM.Application.Memberships;
using GSM.Application.Memberships.Public;
using GSM.Domain.Classes;
using GSM.Domain.Common.Exceptions;
using Microsoft.Extensions.Logging;

namespace GSM.Application.Classes;

/// <summary>Utilidades compartidas por los casos de uso de reservas.</summary>
internal static class ClassBookingSupport
{
    public static void EnsureOccurrence(GymClass gymClass, DateOnly date, ClassSession? existing)
    {
        if (existing is null && !ClassSchedule.OccurrencesOf(gymClass, date, date).Any())
        {
            throw new ValidationDomainException("Esa fecha no corresponde a una ocurrencia de la clase.");
        }
    }

    public static void EnsureNotStarted(IClock clock, ClassSession session)
    {
        var startsLocal = session.SessionDate.ToDateTime(session.StartTime);
        if (clock.ToBusinessLocal(clock.UtcNow) >= startsLocal)
        {
            throw new ValidationDomainException("La sesión ya comenzó.");
        }
    }
}

/// <summary>C1: estar al día es requisito para reservar y para asistir; aquí no hay "Dejar pasar".</summary>
internal static class ClassAccess
{
    /// <summary>409 si el miembro no tiene cobertura (al día, por vencer o en gracia) el día de la sesión.</summary>
    public static async Task<AccessStatus> EnsureCoveredAsync(
        IMembershipReader memberships, Guid memberId, DateOnly sessionDate, string action, CancellationToken cancellationToken)
    {
        var access = await memberships.GetAccessStatusOnAsync(memberId, sessionDate, cancellationToken);
        if (access.CanEnter)
        {
            return access;
        }

        throw new ConflictException(BlockedMessage(access, sessionDate, action));
    }

    public static string BlockedMessage(AccessStatus access, DateOnly sessionDate, string action)
        => access.BlockReason switch
        {
            AccessBlockReason.Frozen => $"Tu membresía está congelada; reanúdala para {action}.",
            AccessBlockReason.Scheduled when access.StartsOn is { } starts =>
                $"Tu plan empieza el {CommercialStatusCalculator.ShortDate(starts)}; no cubre el {CommercialStatusCalculator.ShortDate(sessionDate)} para {action}.",
            AccessBlockReason.PassExhausted =>
                $"Ya usaste todas las entradas de tu tiquetera; renueva para {action}.",
            AccessBlockReason.Expired when access.EndsOn is { } ends =>
                $"Tu plan vence el {CommercialStatusCalculator.ShortDate(ends)}; renueva para {action}.",
            _ => $"No tienes una membresía vigente el {CommercialStatusCalculator.ShortDate(sessionDate)}; renueva para {action}.",
        };
}

/// <summary>El miembro reserva una plaza. 422 si la clase no tiene cupo; 409 si está llena, cancelada, fuera del horario del gym o el miembro no está al día.</summary>
public sealed class BookClassHandler(
    ICurrentUserService currentUser,
    IClock clock,
    IGymReader gyms,
    IMemberReader members,
    IMembershipReader memberships,
    IClassRepository classes,
    IClassSessionRepository sessions,
    IClassBookingRepository bookings,
    IClassSettingsRepository classSettings,
    IBookingBlockRepository blocks,
    IWaitlistRepository waitlist,
    WaitlistService waitlistService,
    ClassCapacityService capacityService,
    IUnitOfWork unitOfWork,
    IDomainEventPublisher events,
    ILogger<BookClassHandler> logger)
{
    public async Task<BookingResponse> HandleAsync(
        Guid classId, DateOnly date, CancellationToken cancellationToken)
    {
        var gymId = currentUser.RequireGymId();
        await ClassFeatureGate.EnsureMemberModuleEnabledAsync(gyms, gymId, cancellationToken);

        var self = await members.ResolveByUserIdAsync(currentUser.UserId ?? Guid.Empty, cancellationToken)
            ?? throw new ForbiddenDomainException("Tu cuenta no está vinculada a un miembro.");

        var gymClass = await classes.GetAsync(classId, cancellationToken)
            ?? throw new NotFoundException("Clase no encontrada.");

        if (gymClass.Capacity is null)
        {
            throw new ValidationDomainException(
                "Esta clase es de entrada libre: no requiere reserva, basta con asistir.");
        }

        var existing = await sessions.GetAsync(classId, date, cancellationToken);
        ClassBookingSupport.EnsureOccurrence(gymClass, date, existing);

        if (existing is { IsCancelled: true })
        {
            throw new ConflictException("La sesión está cancelada.");
        }

        if (existing is not null)
        {
            ClassBookingSupport.EnsureNotStarted(clock, existing);
            var current = await bookings.GetAsync(existing.Id, self.Id, cancellationToken);
            if (current is { IsActive: true })
            {
                return await ResponseAsync(current, existing, classId, self.Id, cancellationToken);
            }
        }

        // C1: la ocurrencia debe caer dentro del horario del gym y el miembro debe estar al día ese día.
        // Va antes de materializar la sesión: un rechazo no deja filas.
        await ClassOpeningHours.EnsureOccurrenceOpenAsync(
            gyms, clock, gymId, date,
            existing?.StartTime ?? gymClass.StartTime, existing?.DurationMinutes ?? gymClass.DurationMinutes,
            cancellationToken);
        await ClassAccess.EnsureCoveredAsync(
            memberships, self.Id, date, "reservar esta clase", cancellationToken);

        // C3: penalización vigente, ventana de reserva y máximo de reservas activas (también antes de materializar).
        var rules = await ClassBookingRules.LoadAsync(classSettings, gymId, cancellationToken);
        await ClassBookingRules.EnsureCanBookAsync(clock, blocks, bookings, waitlist, rules, self.Id, date, cancellationToken);

        var session = existing
            ?? await ClassSessionMaterializer.GetOrCreateAsync(sessions, unitOfWork, gymClass, date, cancellationToken);

        ClassBookingSupport.EnsureNotStarted(clock, session);

        // C4: quien está en la lista de espera confirma su oferta; no reserva por la puerta de al lado.
        if (await waitlist.GetOpenAsync(session.Id, self.Id, cancellationToken) is not null)
        {
            throw new ConflictException(
                "Estás en la lista de espera de esta sesión; confirma tu cupo cuando te lo ofrezcan o sal de la lista.");
        }

        // El cupo se decide bajo el candado de la sesión: los cupos apartados para la lista de espera cuentan como
        // ocupados, y las ofertas vencidas se liberan (o pasan al siguiente) antes de contar.
        ClassBooking? mine = null;
        List<IDomainEvent> raised = [];
        await unitOfWork.ExecuteInTransactionAsync(async ct =>
        {
            await sessions.LockAsync(session.Id, ct);
            raised = await waitlistService.ProcessCoreAsync(session, ct);
            await unitOfWork.SaveChangesAsync(ct);

            mine = await bookings.GetAsync(session.Id, self.Id, ct);

            var active = await bookings.CountActiveAsync(session.Id, ct);
            var offers = await waitlist.CountOffersAsync(session.Id, ct);
            // C6: el cupo efectivo sale de las máquinas ligadas disponibles ese día (perezoso, no se guarda).
            var capacity = await capacityService.ForAsync(gymId, classId, session.Capacity, session.SessionDate, ct);
            if (capacity.Effective is { } cap && active + offers >= cap)
            {
                throw new ConflictException(capacity.LimitedByMachines
                    ? $"La sesión está completa: hay {capacity.AvailableMachines} máquinas disponibles ese día."
                    : "La sesión está completa.");
            }

            if (mine is null)
            {
                mine = ClassBooking.Book(gymId, session.Id, self.Id, clock.UtcNow);
                bookings.Add(mine);
            }
            else
            {
                mine.Rebook(clock.UtcNow);
            }

            await unitOfWork.SaveChangesAsync(ct);
        }, cancellationToken);

        await waitlistService.PublishAsync(raised, cancellationToken);

        logger.LogInformation(
            "Miembro {MemberId} reservó la sesión {SessionId} ({ClassId} {Date})", self.Id, session.Id, classId, date);

        // N5: Notifications programa el recordatorio de clase. Tras el commit, nunca dentro.
        await events.PublishAsync(
            new ClassBooked(gymId, self.Id, classId, session.SessionDate, session.StartTime, session.InstructorUserId),
            cancellationToken);

        return await ResponseAsync(mine!, session, classId, self.Id, cancellationToken);
    }

    private async Task<BookingResponse> ResponseAsync(
        ClassBooking booking, ClassSession session, Guid classId, Guid memberId, CancellationToken cancellationToken)
    {
        var access = await memberships.GetAccessStatusOnAsync(memberId, session.SessionDate, cancellationToken);
        return new BookingResponse(
            booking.Id, session.Id, classId, session.SessionDate, booking.Status.ToString(),
            MembershipExpired: access.Kind == AccessKind.Grace, AccessNotice: access.Notice);
    }
}

/// <summary>
/// El miembro cancela su reserva. Permitido hasta la hora de inicio de la sesión; pasado el plazo de
/// cancelación del gym (C3) se cancela igual, para liberar el cupo, pero cuenta como inasistencia.
/// </summary>
public sealed class CancelBookingHandler(
    ICurrentUserService currentUser,
    IClock clock,
    IGymReader gyms,
    IMemberReader members,
    IClassSessionRepository sessions,
    IClassBookingRepository bookings,
    IClassSettingsRepository classSettings,
    BookingPenaltyService penalties,
    WaitlistService waitlistService,
    IUnitOfWork unitOfWork,
    IDomainEventPublisher events,
    ILogger<CancelBookingHandler> logger)
{
    public async Task<CancelBookingResponse> HandleAsync(Guid classId, DateOnly date, CancellationToken cancellationToken)
    {
        var gymId = currentUser.RequireGymId();
        await ClassFeatureGate.EnsureMemberModuleEnabledAsync(gyms, gymId, cancellationToken);

        var self = await members.ResolveByUserIdAsync(currentUser.UserId ?? Guid.Empty, cancellationToken)
            ?? throw new ForbiddenDomainException("Tu cuenta no está vinculada a un miembro.");

        var session = await sessions.GetAsync(classId, date, cancellationToken)
            ?? throw new NotFoundException("No tienes una reserva en esta sesión.");

        var booking = await bookings.GetAsync(session.Id, self.Id, cancellationToken);
        if (booking is null || !booking.IsActive)
        {
            throw new NotFoundException("No tienes una reserva activa en esta sesión.");
        }

        ClassBookingSupport.EnsureNotStarted(clock, session);

        var rules = await ClassBookingRules.LoadAsync(classSettings, gymId, cancellationToken);
        var late = ClassBookingRules.IsLateCancellation(clock, rules, session);

        booking.CancelByMember(clock.UtcNow, late);
        await unitOfWork.SaveChangesAsync(cancellationToken);

        logger.LogInformation(
            "Miembro {MemberId} canceló su reserva en la sesión {SessionId} (tardía: {Late})", self.Id, session.Id, late);

        // N5: Notifications cancela el recordatorio programado de esa reserva.
        await events.PublishAsync(
            new ClassBookingCancelled(
                gymId, self.Id, classId, session.SessionDate,
                await bookings.CountActiveAsync(session.Id, cancellationToken)),
            cancellationToken);

        // C4: el cupo liberado se le ofrece al primero de la lista de espera.
        await waitlistService.ProcessAsync(session.Id, cancellationToken);

        var block = late ? await penalties.EvaluateAsync(gymId, self.Id, cancellationToken) : null;
        return new CancelBookingResponse(late, block?.BlockedUntil);
    }
}

/// <summary>Reservas y asistencia de una sesión concreta (staff).</summary>
public sealed class GetRosterHandler(
    ICurrentUserService currentUser,
    IMemberReader members,
    IClassRepository classes,
    IClassSessionRepository sessions,
    IClassBookingRepository bookings,
    IWaitlistRepository waitlist,
    WaitlistService waitlistService,
    ClassCapacityService capacityService)
{
    public async Task<RosterResponse> HandleAsync(
        Guid classId, DateOnly date, CancellationToken cancellationToken)
    {
        var gymId = currentUser.RequireGymId();

        var gymClass = await classes.GetAsync(classId, cancellationToken)
            ?? throw new NotFoundException("Clase no encontrada.");

        var session = await sessions.GetAsync(classId, date, cancellationToken);
        ClassBookingSupport.EnsureOccurrence(gymClass, date, session);

        var rows = session is null
            ? []
            : await bookings.ListForSessionAsync(session.Id, cancellationToken);

        // C4: mirar la sesión pone al día su lista de espera (vence ofertas, pasa el cupo al siguiente).
        var waitlistRows = new List<WaitlistRosterEntry>();
        if (session is not null && (await waitlist.ListOpenForSessionAsync(session.Id, cancellationToken)).Count > 0)
        {
            await waitlistService.ProcessAsync(session.Id, cancellationToken);
            var open = await waitlist.ListOpenForSessionAsync(session.Id, cancellationToken);
            rows = await bookings.ListForSessionAsync(session.Id, cancellationToken);
            foreach (var w in open)
            {
                var member = await members.GetAsync(w.MemberId, cancellationToken);
                waitlistRows.Add(new WaitlistRosterEntry(
                    w.Id, w.MemberId,
                    member is null ? "(miembro eliminado)" : $"{member.FirstName} {member.LastName}",
                    WaitlistService.Rank(open, w), w.Status.ToString(), w.OfferExpiresAt));
            }
        }

        var entries = new List<RosterEntry>();
        foreach (var b in rows.OrderBy(b => b.BookedAt))
        {
            var member = await members.GetAsync(b.MemberId, cancellationToken);
            entries.Add(new RosterEntry(
                b.Id, b.MemberId,
                member is null ? "(miembro eliminado)" : $"{member.FirstName} {member.LastName}",
                b.Status.ToString(), b.BookedAt, b.AttendedAt));
        }

        // C6: con máquinas ligadas el roster dice cuántas hay disponibles ese día, para que el staff decida si sobran reservas.
        var capacity = await capacityService.ForAsync(
            gymId, classId, session?.Capacity ?? gymClass.Capacity, date, cancellationToken);

        return new RosterResponse(
            gymClass.Id, gymClass.Name, date,
            session?.StartTime ?? gymClass.StartTime,
            session?.Capacity ?? gymClass.Capacity,
            (session?.Status ?? ClassSessionStatus.Scheduled).ToString(),
            rows.Count(b => b.Status == ClassBookingStatus.Booked),
            entries,
            waitlistRows,
            capacity.IsLinked ? capacity.Effective : null,
            capacity.IsLinked ? capacity.AvailableMachines : null,
            capacity.IsLinked ? capacity.LinkedMachines : null);
    }
}

/// <summary>
/// El staff marca asistentes y ausentes de una sesión. Publica <c>ClassAttendanceRegistered</c> por cada asistente. Un
/// asistente que no está al día no se marca, pero no frena a los demás (C1-4): sale en <c>NotMarked</c> con el motivo.
/// </summary>
public sealed class MarkAttendanceHandler(
    ICurrentUserService currentUser,
    IClock clock,
    IGymReader gyms,
    IMemberReader members,
    IMembershipReader memberships,
    IClassRepository classes,
    IClassSessionRepository sessions,
    IClassBookingRepository bookings,
    IUnitOfWork unitOfWork,
    IDomainEventPublisher events,
    BookingPenaltyService penalties,
    GetRosterHandler roster,
    ILogger<MarkAttendanceHandler> logger)
{
    public async Task<RosterResponse> HandleAsync(
        Guid classId, DateOnly date, MarkAttendanceRequest request, CancellationToken cancellationToken)
    {
        var gymId = currentUser.RequireGymId();

        var gymClass = await classes.GetAsync(classId, cancellationToken)
            ?? throw new NotFoundException("Clase no encontrada.");

        var existing = await sessions.GetAsync(classId, date, cancellationToken);
        ClassBookingSupport.EnsureOccurrence(gymClass, date, existing);

        var session = existing
            ?? await ClassSessionMaterializer.GetOrCreateAsync(sessions, unitOfWork, gymClass, date, cancellationToken);

        if (session.IsCancelled)
        {
            throw new ConflictException("La sesión está cancelada.");
        }

        var requested = (request.Attended ?? []).Distinct().ToList();
        var attended = new List<Guid>(requested);
        var skipped = new List<SkippedAttendee>();
        var noShow = (request.NoShow ?? []).Distinct().Except(requested).ToList();

        // Asistir a una clase cuenta como entrada del día: con el gym cerrado (gym_closed) no se registra,
        // sin excepción. Se mide la clase completa en la fecha de la sesión, no la hora en que el staff la marca.
        // Estar al día también es requisito, y aquí no hay "Dejar pasar": hay que renovar primero. Quien no lo está se
        // deja fuera y se informa; el resto del lote se guarda igual (C1-4).
        var now = clock.UtcNow;
        var current = await bookings.ListForSessionAsync(session.Id, cancellationToken);
        var byMember = current.ToDictionary(b => b.MemberId);

        if (attended.Count > 0)
        {
            await ClassOpeningHours.EnsureOccurrenceOpenAsync(
                gyms, clock, gymId, session.SessionDate, session.StartTime, session.DurationMinutes, cancellationToken);

            foreach (var memberId in requested)
            {
                var memberView = await members.GetAsync(memberId, cancellationToken)
                    ?? throw new NotFoundException($"Miembro {memberId} no encontrado.");

                // Quien ya estaba marcado como asistente no se vuelve a examinar.
                if (byMember.TryGetValue(memberId, out var already) && already.Status == ClassBookingStatus.Attended)
                {
                    continue;
                }

                var access = await memberships.GetAccessStatusOnAsync(memberId, session.SessionDate, cancellationToken);
                if (!access.CanEnter)
                {
                    attended.Remove(memberId);
                    skipped.Add(new SkippedAttendee(
                        memberId,
                        $"{memberView.FirstName} {memberView.LastName}",
                        $"{access.BlockMessage ?? "Sin membresía vigente."} Debe renovar primero."));
                }
            }
        }

        var attendedForEvents = new List<Guid>();

        foreach (var memberId in attended)
        {
            if (!await members.ExistsAsync(memberId, cancellationToken))
            {
                throw new NotFoundException($"Miembro {memberId} no encontrado.");
            }

            if (byMember.TryGetValue(memberId, out var booking))
            {
                booking.MarkAttended(now);
            }
            else
            {
                bookings.Add(ClassBooking.AttendDirectly(gymId, session.Id, memberId, now));
            }

            attendedForEvents.Add(memberId);
        }

        // C3: "no vino" cuenta como inasistencia. Una reserva que nadie marcó no cuenta nunca.
        var struck = new List<Guid>();
        foreach (var memberId in noShow)
        {
            if (byMember.TryGetValue(memberId, out var booking) && booking.Status == ClassBookingStatus.Booked)
            {
                booking.MarkNoShow(now);
                struck.Add(memberId);
            }
        }

        await unitOfWork.SaveChangesAsync(cancellationToken);
        logger.LogInformation(
            "Sesión {SessionId}: {Attended} asistentes, {NoShow} ausentes marcados, {Skipped} sin marcar por no estar al día",
            session.Id, attended.Count, noShow.Count, skipped.Count);

        foreach (var memberId in struck)
        {
            await penalties.EvaluateAsync(gymId, memberId, cancellationToken);
        }

        foreach (var memberId in attendedForEvents)
        {
            await events.PublishAsync(
                new ClassAttendanceRegistered(memberId, gymId, session.SessionDate), cancellationToken);
        }

        var result = await roster.HandleAsync(classId, date, cancellationToken);
        return skipped.Count == 0 ? result : result with { NotMarked = skipped };
    }
}

/// <summary>Histórico de clases del miembro (todas sus reservas y asistencias).</summary>
public sealed class GetMyClassHistoryHandler(
    ICurrentUserService currentUser,
    IGymReader gyms,
    IMemberReader members,
    IClassBookingRepository bookings)
{
    public async Task<IReadOnlyList<ClassHistoryItem>> HandleAsync(CancellationToken cancellationToken)
    {
        var gymId = currentUser.RequireGymId();
        await ClassFeatureGate.EnsureMemberModuleEnabledAsync(gyms, gymId, cancellationToken);

        var self = await members.ResolveByUserIdAsync(currentUser.UserId ?? Guid.Empty, cancellationToken)
            ?? throw new ForbiddenDomainException("Tu cuenta no está vinculada a un miembro.");

        var rows = await bookings.ListForMemberAsync(self.Id, from: null, to: null, cancellationToken);

        return rows
            .OrderByDescending(r => r.Session.SessionDate)
            .ThenByDescending(r => r.Session.StartTime)
            .Select(r => new ClassHistoryItem(
                r.Booking.Id, r.Class.Id, r.Class.Name,
                r.Session.SessionDate, r.Session.StartTime, r.Booking.Status.ToString()))
            .ToList();
    }
}
