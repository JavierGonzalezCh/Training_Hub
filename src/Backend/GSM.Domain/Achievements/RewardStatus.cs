namespace GSM.Domain.Achievements;

/// <summary>
/// Estado del premio de un logro ganado (L5). En la base solo se guardan <c>none</c>, <c>pending</c> y
/// <c>delivered</c>: <see cref="Expired"/> se calcula (pendiente con el plazo vencido), sin tarea programada.
/// </summary>
public enum RewardStatus
{
    None,
    Pending,
    Delivered,
    Expired,
}
