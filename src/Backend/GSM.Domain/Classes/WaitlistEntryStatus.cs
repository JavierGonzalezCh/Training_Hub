namespace GSM.Domain.Classes;

/// <summary>
/// Estado de una entrada de la lista de espera (C4). Enum nativo de Postgres
/// (<c>classes.waitlist_entry_status</c>).
/// </summary>
public enum WaitlistEntryStatus
{
    /// <summary>En la lista, esperando que se libere un cupo.</summary>
    Waiting,

    /// <summary>Se le ofreció un cupo y tiene hasta <c>OfferExpiresAt</c> para confirmar. El cupo está apartado.</summary>
    Offered,

    /// <summary>Confirmó a tiempo: ya tiene su reserva.</summary>
    Confirmed,

    /// <summary>No confirmó a tiempo, o la lista se cerró (empezó la clase, se canceló la sesión).</summary>
    Expired,

    /// <summary>Salió de la lista por decisión propia (salir o rechazar la oferta).</summary>
    Left,
}
