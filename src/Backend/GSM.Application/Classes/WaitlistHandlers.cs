using GSM.Application.Classes.Abstractions;
using GSM.Application.Classes.Contracts;
using GSM.Application.Classes.Events;
using GSM.Application.Common.Abstractions;
using GSM.Application.Common.Events;
using GSM.Application.Gyms.Public;
using GSM.Application.Members.Public;
using GSM.Application.Memberships;
using GSM.Application.Memberships.Public;
using GSM.Domain.Classes;
using GSM.Domain.Common.Exceptions;
using Microsoft.Extensions.Logging;

namespace GSM.Application.Classes;

/// <summary>
/// C4: el miembro se anota en la lista de espera de una sesión llena. Exige lo mismo que reservar (horario del
/// gym, estar al día ese día, penalización, ventana y máximo de reservas, que cuenta la lista de espera). 409 si la
/// sesión aún tiene cupo (hay que reservar), si ya tiene reserva, o está cancelada. Anotarse dos veces devuelve la
/// misma entrada.
/// </summary>
public sealed class JoinWaitlistHandler(
    ICurrentUserService currentUser,
    IClock clock,
    IGymReader gyms,
    IMemberReader members,
    IMembershipReader memberships,
    IClassRepository classes,
    IClassSessionRepository sessions,
    IClassBookingRepository bookings,
    IWaitlistRepository waitlist,
    IClassSettingsRepository classSettings,
    IBookingBlockRepository blocks,
    WaitlistService service,
    ClassCapacityService capacityService,
    IUnitOfWork unitOfWork,
    ILogger<JoinWaitlistHandler> logger)
{
    public async Task<WaitlistResponse> HandleAsync(Guid classId, DateOnly date, CancellationToken cancellationToken)
    {
        var gymId = currentUser.RequireGymId();
        await ClassFeatureGate.EnsureMemberModuleEnabledAsync(gyms, gymId, cancellationToken);

        var self = await members.ResolveByUserIdAsync(currentUser.UserId ?? Guid.Empty, cancellationToken)
            ?? throw new ForbiddenDomainException("Tu cuenta no está vinculada a un miembro.");

        var gymClass = await classes.GetAsync(classId, cancellationToken)
            ?? throw new NotFoundException("Clase no encontrada.");

        if (gymClass.Capacity is null)
        {
            throw new ValidationDomainException("Esta clase es de entrada libre: no tiene lista de espera.");
        }

        var session = await sessions.GetAsync(classId, date, cancellationToken);
        ClassBookingSupport.EnsureOccurrence(gymClass, date, session);

        // Una ocurrencia sin materializar no tiene reservas: no puede estar llena... salvo que las máquinas ligadas
        // disponibles ese día sean cero (C6), y entonces nadie puede reservar y la lista es el único camino.
        if (session is null)
        {
            var open = await capacityService.ForAsync(gymId, classId, gymClass.Capacity, date, cancellationToken);
            if (open.Effective is not 0)
            {
                throw new ConflictException("La sesión tiene cupo; reserva directamente.");
            }

            session = await ClassSessionMaterializer.GetOrCreateAsync(sessions, unitOfWork, gymClass, date, cancellationToken);
        }

        if (session.IsCancelled)
        {
            throw new ConflictException("La sesión está cancelada.");
        }

        ClassBookingSupport.EnsureNotStarted(clock, session);

        var already = await waitlist.GetOpenAsync(session.Id, self.Id, cancellationToken);
        if (already is not null)
        {
            return await WaitlistResponses.BuildAsync(waitlist, already, session, cancellationToken);
        }

        var current = await bookings.GetAsync(session.Id, self.Id, cancellationToken);
        if (current is { IsActive: true })
        {
            throw new ConflictException("Ya tienes una reserva en esta sesión.");
        }

        await ClassOpeningHours.EnsureOccurrenceOpenAsync(
            gyms, clock, gymId, date, session.StartTime, session.DurationMinutes, cancellationToken);
        await ClassAccess.EnsureCoveredAsync(
            memberships, self.Id, date, "anotarte en la lista de espera", cancellationToken);

        var rules = await ClassBookingRules.LoadAsync(classSettings, gymId, cancellationToken);
        await ClassBookingRules.EnsureCanBookAsync(clock, blocks, bookings, waitlist, rules, self.Id, date, cancellationToken);

        WaitlistEntry? entry = null;
        List<IDomainEvent> raised = [];

        await unitOfWork.ExecuteInTransactionAsync(async ct =>
        {
            await sessions.LockAsync(session.Id, ct);

            // Primero se pone al día la lista (vence ofertas, ofrece cupos libres) y se guarda para contar bien.
            raised = await service.ProcessCoreAsync(session, ct);
            await unitOfWork.SaveChangesAsync(ct);

            var booked = await bookings.CountActiveAsync(session.Id, ct);
            var offers = await waitlist.CountOffersAsync(session.Id, ct);
            var capacity = await capacityService.ForAsync(gymId, classId, session.Capacity, session.SessionDate, ct);
            if (capacity.Effective is not { } cap || booked + offers < cap)
            {
                throw new ConflictException("La sesión tiene cupo; reserva directamente.");
            }

            var position = await waitlist.GetMaxPositionAsync(session.Id, ct) + 1;
            entry = WaitlistEntry.Join(gymId, session.Id, self.Id, position);
            waitlist.Add(entry);
            await unitOfWork.SaveChangesAsync(ct);
        }, cancellationToken);

        logger.LogInformation(
            "Miembro {MemberId} se anotó en la lista de espera de la sesión {SessionId}", self.Id, session.Id);

        await service.PublishAsync(raised, cancellationToken);
        return await WaitlistResponses.BuildAsync(waitlist, entry!, session, cancellationToken);
    }
}

/// <summary>
/// C4: el miembro confirma la oferta de cupo dentro del plazo y queda con su reserva. Vencida (o con la lista
/// cerrada) es 409 y el cupo ya pasó al siguiente. La carrera por el mismo cupo se serializa con el candado de la sesión.
/// </summary>
public sealed class ConfirmWaitlistOfferHandler(
    ICurrentUserService currentUser,
    IClock clock,
    IGymReader gyms,
    IMemberReader members,
    IMembershipReader memberships,
    IClassSessionRepository sessions,
    IClassBookingRepository bookings,
    IWaitlistRepository waitlist,
    IBookingBlockRepository blocks,
    WaitlistService service,
    IUnitOfWork unitOfWork,
    IDomainEventPublisher events,
    ILogger<ConfirmWaitlistOfferHandler> logger)
{
    public async Task<BookingResponse> HandleAsync(Guid classId, DateOnly date, CancellationToken cancellationToken)
    {
        var gymId = currentUser.RequireGymId();
        await ClassFeatureGate.EnsureMemberModuleEnabledAsync(gyms, gymId, cancellationToken);

        var self = await members.ResolveByUserIdAsync(currentUser.UserId ?? Guid.Empty, cancellationToken)
            ?? throw new ForbiddenDomainException("Tu cuenta no está vinculada a un miembro.");

        var session = await sessions.GetAsync(classId, date, cancellationToken)
            ?? throw new NotFoundException("No tienes una oferta en esta sesión.");

        // Estar al día ese día y no tener penalización: lo mismo que para reservar (ventana y máximo ya se
        // comprobaron al anotarse, y la entrada de la lista ya cuenta para el máximo).
        var access = await ClassAccess.EnsureCoveredAsync(
            memberships, self.Id, date, "confirmar tu cupo", cancellationToken);
        if (await blocks.GetActiveAsync(self.Id, clock.Today, cancellationToken) is { } block)
        {
            throw new ConflictException(
                $"No puedes reservar hasta el {CommercialStatusCalculator.ShortDate(block.BlockedUntil)} por inasistencias.");
        }

        ClassBooking? booking = null;
        string? failure = null;
        List<IDomainEvent> raised = [];

        await unitOfWork.ExecuteInTransactionAsync(async ct =>
        {
            await sessions.LockAsync(session.Id, ct);

            var mine = await waitlist.GetOpenAsync(session.Id, self.Id, ct);
            raised = await service.ProcessCoreAsync(session, ct);

            if (mine is null)
            {
                failure = "No tienes una oferta vigente en esta sesión.";
            }
            else if (mine.Status != WaitlistEntryStatus.Offered)
            {
                // La pasada de arriba la venció (o cerró la lista): se confirma ese cambio y se avisa tras el commit.
                failure = "La oferta venció y el cupo pasó al siguiente.";
            }
            else
            {
                mine.Confirm(clock.UtcNow);
                booking = await bookings.GetAsync(session.Id, self.Id, ct);
                if (booking is null)
                {
                    booking = ClassBooking.Book(gymId, session.Id, self.Id, clock.UtcNow);
                    bookings.Add(booking);
                }
                else
                {
                    booking.Rebook(clock.UtcNow);
                }
            }

            await unitOfWork.SaveChangesAsync(ct);
        }, cancellationToken);

        await service.PublishAsync(raised, cancellationToken);

        if (failure is not null)
        {
            throw new ConflictException(failure);
        }

        logger.LogInformation(
            "Miembro {MemberId} confirmó su cupo de la lista de espera en la sesión {SessionId}", self.Id, session.Id);

        // N5: Notifications programa el recordatorio de clase de esta reserva.
        await events.PublishAsync(
            new ClassBooked(gymId, self.Id, classId, session.SessionDate, session.StartTime, session.InstructorUserId),
            cancellationToken);

        return new BookingResponse(
            booking!.Id, session.Id, classId, session.SessionDate, booking.Status.ToString(),
            MembershipExpired: access.Kind == AccessKind.Grace, AccessNotice: access.Notice);
    }
}

/// <summary>
/// C4: el miembro sale de la lista de espera o rechaza la oferta. Si tenía una oferta, el cupo pasa de inmediato al
/// siguiente. Salir y volver a anotarse lo deja al final.
/// </summary>
public sealed class LeaveWaitlistHandler(
    ICurrentUserService currentUser,
    IGymReader gyms,
    IMemberReader members,
    IClassSessionRepository sessions,
    IWaitlistRepository waitlist,
    WaitlistService service,
    IUnitOfWork unitOfWork,
    ILogger<LeaveWaitlistHandler> logger)
{
    public async Task HandleAsync(Guid classId, DateOnly date, CancellationToken cancellationToken)
    {
        var gymId = currentUser.RequireGymId();
        await ClassFeatureGate.EnsureMemberModuleEnabledAsync(gyms, gymId, cancellationToken);

        var self = await members.ResolveByUserIdAsync(currentUser.UserId ?? Guid.Empty, cancellationToken)
            ?? throw new ForbiddenDomainException("Tu cuenta no está vinculada a un miembro.");

        var session = await sessions.GetAsync(classId, date, cancellationToken)
            ?? throw new NotFoundException("No estás en la lista de espera de esta sesión.");

        List<IDomainEvent> raised = [];

        await unitOfWork.ExecuteInTransactionAsync(async ct =>
        {
            await sessions.LockAsync(session.Id, ct);

            var entry = await waitlist.GetOpenAsync(session.Id, self.Id, ct)
                ?? throw new NotFoundException("No estás en la lista de espera de esta sesión.");
            entry.Leave();
            await unitOfWork.SaveChangesAsync(ct);

            // Si tenía la oferta, el cupo apartado queda libre para el siguiente.
            raised = await service.ProcessCoreAsync(session, ct);
            await unitOfWork.SaveChangesAsync(ct);
        }, cancellationToken);

        logger.LogInformation("Miembro {MemberId} salió de la lista de espera de la sesión {SessionId}", self.Id, session.Id);
        await service.PublishAsync(raised, cancellationToken);
    }
}

/// <summary>
/// C4: tarea periódica (cada 5–10 min, junto a las demás). Revisa las sesiones con lista de espera abierta: vence
/// las ofertas pasadas, pasa el cupo al siguiente y cierra las listas de sesiones que ya empezaron. Idempotente y
/// tolera ejecuciones perdidas: lo que no hizo la tarea lo hace la siguiente consulta de la sesión.
/// </summary>
public sealed class RunWaitlistJobHandler(
    IWaitlistRepository waitlist,
    WaitlistService service,
    ILogger<RunWaitlistJobHandler> logger)
{
    public async Task<WaitlistProcessResult> HandleAsync(CancellationToken cancellationToken)
    {
        var sessionIds = await waitlist.ListSessionIdsWithOpenEntriesAsync(cancellationToken);
        var made = 0;
        var expired = 0;
        foreach (var sessionId in sessionIds)
        {
            var result = await service.ProcessAsync(sessionId, cancellationToken);
            made += result.OffersMade;
            expired += result.OffersExpired;
        }

        logger.LogInformation(
            "Lista de espera: {Sessions} sesiones revisadas, {Made} ofertas, {Expired} vencidas", sessionIds.Count, made, expired);
        return new WaitlistProcessResult(made, expired);
    }
}

internal static class WaitlistResponses
{
    public static async Task<WaitlistResponse> BuildAsync(
        IWaitlistRepository waitlist, WaitlistEntry entry, ClassSession session, CancellationToken cancellationToken)
    {
        var open = await waitlist.ListOpenForSessionAsync(session.Id, cancellationToken);
        return new WaitlistResponse(
            entry.Id, session.Id, session.ClassId, session.SessionDate, entry.Status.ToString(),
            WaitlistService.Rank(open, entry), entry.OfferExpiresAt);
    }
}
