namespace GSM.Domain.Memberships;

/// <summary>Tipo de plan (Memberships v2, §3): por tiempo o tiquetera.</summary>
public enum PlanKind
{
    /// <summary>Cubre un periodo (en días o en meses del calendario).</summary>
    Time,

    /// <summary>Tiquetera: N entradas que se deben usar dentro de una vigencia. Sin gracia (§5.1).</summary>
    Pass,
}
