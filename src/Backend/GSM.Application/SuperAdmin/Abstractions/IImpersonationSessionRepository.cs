using GSM.Domain.SuperAdmin;

namespace GSM.Application.SuperAdmin.Abstractions;

public interface IImpersonationSessionRepository
{
    void Add(ImpersonationSession session);

    Task<ImpersonationSession?> GetByIdAsync(Guid sessionId, CancellationToken cancellationToken = default);

    /// <summary>Sesión abierta (sin cerrar) de ese superadmin, si la hay.</summary>
    Task<ImpersonationSession?> GetActiveForUserAsync(
        Guid superAdminUserId, CancellationToken cancellationToken = default);

    /// <summary>Historial de auditoría, del más reciente al más antiguo, con el nombre del gym.</summary>
    Task<IReadOnlyList<ImpersonationAuditRow>> GetHistoryAsync(
        Guid? gymId, int limit, CancellationToken cancellationToken = default);

    /// <summary>Acciones de una sesión, de la más antigua a la más reciente.</summary>
    Task<IReadOnlyList<ImpersonationAction>> GetActionsAsync(
        Guid sessionId, int limit, CancellationToken cancellationToken = default);
}

/// <summary>
/// Registra una acción de una sesión de impersonación <b>fuera</b> de la transacción de la petición
/// (con su propio ámbito y su propio contexto). Nunca lanza: si guardar falla, se loguea y la
/// petición sigue.
/// </summary>
public interface IImpersonationActionRecorder
{
    Task RecordAsync(
        Guid sessionId,
        string method,
        string endpoint,
        string summary,
        int statusCode,
        CancellationToken cancellationToken = default);
}

public sealed record ImpersonationAuditRow(
    Guid Id,
    Guid SuperAdminUserId,
    Guid GymId,
    string GymName,
    string Reason,
    ImpersonationMode Mode,
    int ActionCount,
    DateTimeOffset StartedAt,
    DateTimeOffset? EndedAt);
