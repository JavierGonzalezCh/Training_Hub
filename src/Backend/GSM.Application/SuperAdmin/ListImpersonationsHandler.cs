using GSM.Application.SuperAdmin.Abstractions;
using GSM.Application.SuperAdmin.Contracts;

namespace GSM.Application.SuperAdmin;

/// <summary>Historial de auditoría de impersonaciones: quién, dónde, cuándo y por qué.</summary>
public sealed class ListImpersonationsHandler(IImpersonationSessionRepository sessions)
{
    public async Task<IReadOnlyList<ImpersonationItem>> HandleAsync(
        Guid? gymId, int limit, CancellationToken cancellationToken)
    {
        var rows = await sessions.GetHistoryAsync(gymId, limit <= 0 ? 100 : limit, cancellationToken);

        return rows
            .Select(r => new ImpersonationItem(
                r.Id, r.SuperAdminUserId, r.GymId, r.GymName, r.Reason, r.StartedAt, r.EndedAt))
            .ToList();
    }
}
