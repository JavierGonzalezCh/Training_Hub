using GSM.Domain.SuperAdmin;

namespace GSM.Application.SuperAdmin.Public;

/// <summary>
/// Lectura que SuperAdmin expone a los demás módulos y a la API para que consulten el estado
/// comercial efectivo de un gym sin conocer este módulo. Resuelve por id explícito, ignorando
/// el Global Query Filter (contrato predecible sin importar el contexto del llamador).
/// </summary>
public interface ISubscriptionReader
{
    /// <summary>Estado efectivo del gym hoy, o <c>null</c> si el gym no tiene suscripción.</summary>
    Task<EffectiveSubscriptionStatus?> GetEffectiveStatusAsync(
        Guid gymId, CancellationToken cancellationToken = default);

    /// <summary>
    /// <c>true</c> si el gym puede escribir: cualquier estado salvo <c>disabled</c> y <c>suspended</c>.
    /// Un gym <b>sin suscripción</b> devuelve <c>true</c>: ante el dato faltante se deja pasar.
    /// </summary>
    Task<bool> IsWriteAllowedAsync(Guid gymId, CancellationToken cancellationToken = default);

    /// <summary>Datos para el bloque <c>subscription</c> de <c>GET /api/gym</c> y <c>/api/auth/me</c>.</summary>
    Task<SubscriptionStatusView?> GetStatusViewAsync(
        Guid gymId, CancellationToken cancellationToken = default);
}

public sealed record SubscriptionStatusView(
    EffectiveSubscriptionStatus Status,
    DateOnly PaidThrough,
    int? DaysUntilDisabled);

/// <summary>
/// Bloque <c>subscription</c> para las respuestas de la API (<c>GET /api/gym</c>, <c>/api/auth/me</c>).
/// El estado va como texto, igual que el resto de enums de la API.
/// </summary>
public sealed record SubscriptionInfo(string Status, DateOnly PaidThrough, int? DaysUntilDisabled)
{
    public static SubscriptionInfo? From(SubscriptionStatusView? view)
        => view is null
            ? null
            : new SubscriptionInfo(view.Status.ToString(), view.PaidThrough, view.DaysUntilDisabled);
}
