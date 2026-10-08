namespace GSM.Domain.Memberships;

/// <summary>
/// Cómo se calculó el inicio de una membresía (Memberships v2, §4.1). Sirve para reportes y para
/// saber por qué la fecha de inicio es la que es.
/// </summary>
public enum RenewalKind
{
    /// <summary>Nunca tuvo membresía (o ninguna vigente): empieza hoy o la fecha que elija recepción.</summary>
    New,

    /// <summary>Renueva antes de vencer: empieza el día siguiente al fin de la última.</summary>
    Early,

    /// <summary>Vencida, dentro de la gracia: conserva su ciclo (día siguiente al fin de la vencida).</summary>
    InGrace,

    /// <summary>Vencida, pasada la gracia: empieza el día en que paga.</summary>
    AfterGrace,
}
