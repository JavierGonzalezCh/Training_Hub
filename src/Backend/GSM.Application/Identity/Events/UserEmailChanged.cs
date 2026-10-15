using GSM.Application.Common.Events;

namespace GSM.Application.Identity.Events;

/// <summary>
/// La persona confirmó un email nuevo para su cuenta (Identity §3.4). Members actualiza el email de todas las
/// fichas vinculadas a esa cuenta, en todos sus gyms ("un solo email, manda Members"). Los manejadores deben
/// ser idempotentes: un reintento no puede duplicar ni deshacer el cambio.
/// </summary>
public sealed record UserEmailChanged(Guid UserId, string OldEmail, string NewEmail) : IDomainEvent;
