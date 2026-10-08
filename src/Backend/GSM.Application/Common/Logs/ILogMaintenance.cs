namespace GSM.Application.Common.Logs;

/// <summary>Mantenimiento del schema <c>logs</c>: particiones mensuales y retención de 1 año (§9).</summary>
public interface ILogMaintenance
{
    /// <summary>Retención de los registros, en días ("1 año para todo").</summary>
    public const int RetentionDays = 365;

    /// <summary>
    /// Corre la purga diaria si hoy (día de negocio) todavía no corrió: crea las particiones de <c>errors</c> que
    /// falten, suelta las que quedaron por completo fuera de la retención y borra lo viejo de las demás tablas.
    /// Devuelve <c>null</c> si ya había corrido hoy.
    /// </summary>
    Task<LogPurgeResult?> RunDailyIfDueAsync(CancellationToken cancellationToken = default);
}

public sealed record LogPurgeResult(
    int PartitionsCreated,
    int PartitionsDropped,
    int RowsDeleted);
