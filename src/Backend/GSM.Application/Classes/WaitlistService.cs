using GSM.Application.Classes.Abstractions;
using GSM.Application.Classes.Events;
using GSM.Application.Common.Abstractions;
using GSM.Application.Common.Events;
using GSM.Domain.Classes;
using Microsoft.Extensions.Logging;

namespace GSM.Application.Classes;

/// <summary>Lo que hizo una pasada sobre la lista de espera de una sesión.</summary>
public sealed record WaitlistProcessResult(int OffersMade, int OffersExpired);

/// <summary>
/// C4: el motor de la lista de espera. Como no hay scheduler propio, la lista se pone al día de forma perezosa:
/// lo invocan la cancelación de una reserva, el roster del staff, el horario del miembro y la tarea periódica
/// (<see cref="RunWaitlistJobHandler"/>). Cada pasada hace, en este orden:
/// <list type="number">
/// <item>vence las ofertas cuya hora límite pasó (<c>offered → expired</c>, con aviso al miembro);</item>
/// <item>si la sesión ya empezó, se canceló o no tiene cupo, cierra la lista (<c>expired</c>, sin aviso);</item>
/// <item>si hay cupos libres (cupo − reservas − ofertas vigentes), ofrece uno a cada primero de la lista.</item>
/// </list>
/// Una oferta puede durar unos minutos más de lo anunciado (hasta la siguiente pasada), nunca menos.
/// Todo cambio de cupo de la sesión se serializa con <see cref="IClassSessionRepository.LockAsync"/>.
/// </summary>
public sealed class WaitlistService(
    IClock clock,
    IClassSessionRepository sessions,
    IClassBookingRepository bookings,
    IWaitlistRepository waitlist,
    IClassSettingsRepository settings,
    IUnitOfWork unitOfWork,
    IDomainEventPublisher events,
    ILogger<WaitlistService> logger)
{
    /// <summary>Una oferta nunca llega más allá de este margen antes del inicio de la clase (spec C4: 15 minutos).</summary>
    public static readonly TimeSpan OfferCutoff = TimeSpan.FromMinutes(15);

    /// <summary>
    /// Pasada completa sobre una sesión: transacción con candado, guarda y publica los eventos tras el commit.
    /// Idempotente: repetirla no ofrece dos veces ni vence dos veces.
    /// </summary>
    public async Task<WaitlistProcessResult> ProcessAsync(Guid sessionId, CancellationToken cancellationToken)
    {
        List<IDomainEvent> raised = [];

        await unitOfWork.ExecuteInTransactionAsync(async ct =>
        {
            await sessions.LockAsync(sessionId, ct);
            var session = await sessions.GetByIdAsync(sessionId, ct);
            if (session is null)
            {
                raised = [];
                return;
            }

            raised = await ProcessCoreAsync(session, ct);
            await unitOfWork.SaveChangesAsync(ct);
        }, cancellationToken);

        await PublishAsync(raised, cancellationToken);
        return Summarize(raised);
    }

    /// <summary>
    /// La pasada sin transacción ni guardado: el llamante ya tiene el candado de la sesión, guarda y publica
    /// los eventos que devuelve después del commit.
    /// </summary>
    public async Task<List<IDomainEvent>> ProcessCoreAsync(ClassSession session, CancellationToken cancellationToken)
    {
        var raised = new List<IDomainEvent>();
        var open = await waitlist.ListOpenForSessionAsync(session.Id, cancellationToken);
        if (open.Count == 0)
        {
            return raised;
        }

        var now = clock.UtcNow;

        foreach (var entry in open.Where(e => e.IsOfferExpired(now)).ToList())
        {
            entry.Expire();
            raised.Add(new WaitlistOfferExpired(
                session.GymId, entry.MemberId, session.ClassId, entry.Id, session.SessionDate));
        }

        var started = clock.ToBusinessLocal(now) >= session.SessionDate.ToDateTime(session.StartTime);
        if (session.IsCancelled || started || session.Capacity is null)
        {
            foreach (var entry in open)
            {
                entry.Expire(); // la lista se cierra: sin aviso, no hubo oferta que vencer
            }

            return raised;
        }

        var waiting = open.Where(e => e.Status == WaitlistEntryStatus.Waiting).OrderBy(e => e.Position).ToList();
        if (waiting.Count == 0)
        {
            return raised;
        }

        var booked = await bookings.CountActiveAsync(session.Id, cancellationToken);
        var offered = open.Count(e => e.Status == WaitlistEntryStatus.Offered);
        var free = session.Capacity.Value - booked - offered;
        if (free <= 0)
        {
            return raised;
        }

        var rules = await ClassBookingRules.LoadAsync(settings, session.GymId, cancellationToken);
        if (OfferDeadline(session, now, rules.WaitlistOfferMinutes) is not { } expiresAt)
        {
            return raised; // queda menos de 15 minutos para la clase: ya no tiene sentido ofrecer
        }

        foreach (var entry in waiting.Take(free))
        {
            entry.Offer(now, expiresAt);
            raised.Add(new WaitlistOfferMade(
                session.GymId, entry.MemberId, session.ClassId, entry.Id,
                session.SessionDate, session.StartTime, expiresAt));
            logger.LogInformation(
                "Cupo ofrecido al miembro {MemberId} en la sesión {SessionId} hasta {ExpiresAt}",
                entry.MemberId, session.Id, expiresAt);
        }

        return raised;
    }

    /// <summary>
    /// Hora límite de una oferta hecha ahora: <paramref name="offerMinutes"/> desde ahora, y nunca más allá del
    /// inicio; si no cabe, hasta 15 minutos antes del inicio. <c>null</c> si esa hora ya pasó.
    /// </summary>
    public DateTimeOffset? OfferDeadline(ClassSession session, DateTimeOffset now, int offerMinutes)
    {
        var startUtc = clock.BusinessDayRange(session.SessionDate).Start + session.StartTime.ToTimeSpan();
        var expires = now.AddMinutes(offerMinutes);
        if (expires > startUtc)
        {
            expires = startUtc - OfferCutoff;
        }

        return expires > now ? expires : null;
    }

    public Task PublishAsync(IReadOnlyCollection<IDomainEvent> raised, CancellationToken cancellationToken)
        => raised.Count == 0 ? Task.CompletedTask : events.PublishAsync(raised, cancellationToken);

    public static WaitlistProcessResult Summarize(IEnumerable<IDomainEvent> raised)
    {
        var list = raised.ToList();
        return new WaitlistProcessResult(
            list.OfType<WaitlistOfferMade>().Count(), list.OfType<WaitlistOfferExpired>().Count());
    }

    /// <summary>Lugar en la lista (1 = el primero) entre las entradas abiertas de la sesión.</summary>
    public static int Rank(IEnumerable<WaitlistEntry> openOfSession, WaitlistEntry entry)
        => openOfSession.Count(e => e.Position <= entry.Position);
}
