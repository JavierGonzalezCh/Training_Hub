namespace GSM.Application.Common;

/// <summary>
/// Bloque <c>subscription</c> de las respuestas de la API (<c>GET /api/gym</c>, <c>/api/auth/me</c>):
/// el estado comercial efectivo va como texto, igual que el resto de enums de la API.
/// </summary>
/// <remarks>
/// Vive en <c>Common</c> y solo con primitivos para que los contratos de Gyms e Identity no dependan
/// de SuperAdmin. Lo compone la API a partir de <c>ISubscriptionReader</c>.
/// </remarks>
public sealed record SubscriptionInfo(string Status, DateOnly PaidThrough, int? DaysUntilDisabled);
