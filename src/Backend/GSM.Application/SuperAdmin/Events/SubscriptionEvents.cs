using System.Security.Cryptography;
using System.Text;
using GSM.Application.Common.Events;

namespace GSM.Application.SuperAdmin.Events;

/// <summary>Qué aviso de la suscripción toca darles a los Owners de un gym (SuperAdmin §V3).</summary>
public enum SubscriptionNoticeKind
{
    TrialEnding,
    TrialEnded,
    Expiring,
    ExpiresToday,
    Overdue,
    DisableSoon,
    Disabled,
    Suspended,
    Reactivated,
    PaymentRegistered,
    MemberLimitNear,
    MemberLimitReached,
}

/// <summary>
/// S3: a los Owners de <paramref name="GymId"/> les toca un aviso de la suscripción. Lo publican la tarea periódica
/// (los hitos del calendario y el límite de miembros) y los casos de uso que suspenden, reactivan o registran un pago.
/// Notifications crea un aviso por Owner. <paramref name="ReferenceId"/> es la clave de idempotencia: repetir el evento
/// (la tarea corre cada pocos minutos, o se reintenta un manejador) no duplica el aviso.
/// </summary>
/// <param name="Date">La fecha que cuenta el texto: último día de acceso completo, fin de la prueba, cobertura del pago…</param>
/// <param name="Days">Días que faltan para <paramref name="Date"/> (0 = hoy), cuando el texto los usa.</param>
/// <param name="Amount">Monto del pago (solo <see cref="SubscriptionNoticeKind.PaymentRegistered"/>).</param>
/// <param name="Used">Miembros que cuentan (solo avisos de límite).</param>
/// <param name="Limit">Límite de miembros (solo avisos de límite).</param>
public sealed record SubscriptionNoticeDue(
    Guid GymId,
    SubscriptionNoticeKind Kind,
    Guid ReferenceId,
    DateOnly? Date,
    int? Days,
    decimal? Amount,
    int? Used,
    int? Limit) : IDomainEvent;

/// <summary>Claves de idempotencia de los avisos de la suscripción.</summary>
public static class SubscriptionNoticeReference
{
    /// <summary>
    /// Id estable derivado del gym, el aviso y el hecho de calendario que lo origina (el fin de la prueba, el último
    /// día cubierto, el límite vigente). Un periodo nuevo —otro pago— produce otra clave y por tanto otro aviso.
    /// </summary>
    public static Guid For(Guid gymId, SubscriptionNoticeKind kind, string key)
        => new(MD5.HashData(Encoding.UTF8.GetBytes($"{gymId:N}:subscription:{kind}:{key}")));

    public static Guid For(Guid gymId, SubscriptionNoticeKind kind, DateOnly day)
        => For(gymId, kind, day.ToString("yyyy-MM-dd"));
}
