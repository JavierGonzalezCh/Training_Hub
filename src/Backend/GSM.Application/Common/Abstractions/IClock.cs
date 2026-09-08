namespace GSM.Application.Common.Abstractions;

/// <summary>
/// Única fuente de tiempo de la aplicación. Separa dos conceptos que no son intercambiables:
/// el instante (siempre UTC) y el día de negocio (siempre la fecha local del gimnasio).
/// </summary>
/// <remarks>
/// Colombia es UTC-5, así que entre las 19:00 y la medianoche locales la fecha UTC ya es la del
/// día siguiente. Derivar un <see cref="DateOnly"/> de negocio desde UTC adelanta el día en esa
/// franja: una membresía vendida de noche arrancaría mañana y una que vence hoy se leería como
/// vencida. Por eso <see cref="Today"/> nunca se calcula desde <see cref="UtcNow"/>.
/// </remarks>
public interface IClock
{
    /// <summary>Instante actual en UTC. Para timestamps (<c>created_at</c>, <c>paid_at</c>, expiraciones de token).</summary>
    DateTimeOffset UtcNow { get; }

    /// <summary>
    /// Día de negocio actual en la zona horaria del gimnasio. Para toda fecha que un humano
    /// reconoce como "hoy": vigencias de membresía, alta de miembro, asistencia.
    /// </summary>
    DateOnly Today { get; }

    /// <summary>
    /// Ventana <c>[Start, End)</c> en UTC que cubre un día de negocio en la zona del gimnasio.
    /// Para agrupar por día instantes almacenados en UTC (cierre de caja, filtros por fecha).
    /// </summary>
    (DateTimeOffset Start, DateTimeOffset End) BusinessDayRange(DateOnly businessDay);
}
