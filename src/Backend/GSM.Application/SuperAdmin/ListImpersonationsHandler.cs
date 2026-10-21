using GSM.Application.Common.Abstractions;
using GSM.Application.SuperAdmin.Abstractions;
using GSM.Application.SuperAdmin.Contracts;
using GSM.Domain.SuperAdmin;

namespace GSM.Application.SuperAdmin;

/// <summary>Historial de auditoría de impersonaciones: quién, dónde, cuándo y por qué.</summary>
public sealed class ListImpersonationsHandler(IImpersonationSessionRepository sessions, IClock clock)
{
    public async Task<IReadOnlyList<ImpersonationItem>> HandleAsync(
        Guid? gymId, int limit, CancellationToken cancellationToken)
    {
        var rows = await sessions.GetHistoryAsync(gymId, limit <= 0 ? 100 : limit, cancellationToken);

        // Una sesión que nadie cerró con "Finalizar" termina sola cuando vence su token (B15); no se persiste.
        var now = clock.UtcNow;

        return rows
            .Select(r => new ImpersonationItem(
                r.Id, r.SuperAdminUserId, r.GymId, r.GymName, r.Reason,
                ImpersonationModeName.ToClaim(r.Mode), r.ActionCount, r.StartedAt,
                r.EndedAt ?? (r.StartedAt + ImpersonationSession.TokenLifetime <= now ? r.StartedAt + ImpersonationSession.TokenLifetime : null)))
            .ToList();
    }
}
