namespace GSM.Domain.Memberships;

/// <summary>
/// Estado de una membresía. <see cref="Expired"/> **no se persiste**: se deriva de
/// <c>end_date &lt; today</c> al consultar. En almacenamiento solo viven <see cref="Active"/> y
/// <see cref="Paused"/>; el label existe en el enum de Postgres para correcciones manuales.
/// </summary>
public enum MembershipStatus
{
    Active,
    Expired,
    Paused,
}
