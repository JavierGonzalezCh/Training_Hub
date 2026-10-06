namespace GSM.Domain.Memberships;

/// <summary>
/// Estado de una membresía. <see cref="Expired"/> **no se persiste**: se deriva de
/// <c>end_date &lt; today</c> al consultar. En almacenamiento viven <see cref="Active"/>,
/// <see cref="Paused"/> (congelada) y <see cref="Cancelled"/> (asignada por error o anulada);
/// el label <c>expired</c> existe en el enum de Postgres para correcciones manuales.
/// </summary>
public enum MembershipStatus
{
    Active,
    Expired,
    Paused,
    Cancelled,
}
