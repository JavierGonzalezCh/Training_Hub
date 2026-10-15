using GSM.Domain.Common;
using GSM.Domain.Common.Exceptions;

namespace GSM.Domain.Classes;

/// <summary>
/// Un miembro en la lista de espera de una sesión llena (Classes v2, C4). El orden lo da
/// <see cref="Position"/>, asignada al anotarse (única por sesión). Salir y volver a anotarse crea una
/// fila nueva al final: quien sale pierde su lugar. Una sola entrada abierta (<c>waiting</c> u
/// <c>offered</c>) por miembro y sesión.
/// </summary>
public sealed class WaitlistEntry : BaseEntity, ITenantEntity
{
    private WaitlistEntry()
    {
    }

    public Guid GymId { get; private set; }

    public Guid SessionId { get; private set; }

    public Guid MemberId { get; private set; }

    public int Position { get; private set; }

    public WaitlistEntryStatus Status { get; private set; }

    public DateTimeOffset? OfferedAt { get; private set; }

    public DateTimeOffset? OfferExpiresAt { get; private set; }

    /// <summary>En la lista: esperando o con una oferta en curso.</summary>
    public bool IsOpen => Status is WaitlistEntryStatus.Waiting or WaitlistEntryStatus.Offered;

    public static WaitlistEntry Join(Guid gymId, Guid sessionId, Guid memberId, int position)
    {
        if (gymId == Guid.Empty || sessionId == Guid.Empty || memberId == Guid.Empty)
        {
            throw new ValidationDomainException("Datos de la lista de espera incompletos.");
        }

        if (position < 1)
        {
            throw new ValidationDomainException("La posición en la lista debe ser positiva.");
        }

        return new WaitlistEntry
        {
            GymId = gymId,
            SessionId = sessionId,
            MemberId = memberId,
            Position = position,
            Status = WaitlistEntryStatus.Waiting,
        };
    }

    /// <summary>Aparta un cupo para este miembro hasta <paramref name="expiresAt"/>.</summary>
    public void Offer(DateTimeOffset now, DateTimeOffset expiresAt)
    {
        if (Status != WaitlistEntryStatus.Waiting)
        {
            throw new ConflictException("Solo se puede ofrecer un cupo a quien está esperando.");
        }

        if (expiresAt <= now)
        {
            throw new ValidationDomainException("La oferta debe vencer en el futuro.");
        }

        Status = WaitlistEntryStatus.Offered;
        OfferedAt = now;
        OfferExpiresAt = expiresAt;
    }

    public bool IsOfferExpired(DateTimeOffset now) => Status == WaitlistEntryStatus.Offered && OfferExpiresAt <= now;

    /// <summary>El miembro confirma la oferta dentro del plazo; el llamante crea o reactiva la reserva.</summary>
    public void Confirm(DateTimeOffset now)
    {
        if (Status != WaitlistEntryStatus.Offered)
        {
            throw new ConflictException("No tienes una oferta vigente en esta sesión.");
        }

        if (IsOfferExpired(now))
        {
            throw new ConflictException("La oferta venció y el cupo pasó al siguiente.");
        }

        Status = WaitlistEntryStatus.Confirmed;
    }

    /// <summary>La oferta venció sin respuesta, o la lista se cerró. No hace nada si ya no está abierta.</summary>
    public void Expire()
    {
        if (IsOpen)
        {
            Status = WaitlistEntryStatus.Expired;
        }
    }

    /// <summary>El miembro sale de la lista o rechaza la oferta.</summary>
    public void Leave()
    {
        if (!IsOpen)
        {
            throw new ConflictException("No estás en la lista de espera de esta sesión.");
        }

        Status = WaitlistEntryStatus.Left;
    }
}
