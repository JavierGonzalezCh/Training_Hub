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
}

public sealed record ImpersonationAuditRow(
    Guid Id,
    Guid SuperAdminUserId,
    Guid GymId,
    string GymName,
    string Reason,
    DateTimeOffset StartedAt,
    DateTimeOffset? EndedAt);
