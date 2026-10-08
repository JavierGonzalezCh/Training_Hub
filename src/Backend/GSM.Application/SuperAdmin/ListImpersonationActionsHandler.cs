using GSM.Application.SuperAdmin.Abstractions;
using GSM.Application.SuperAdmin.Contracts;
using GSM.Domain.Common.Exceptions;

namespace GSM.Application.SuperAdmin;

/// <summary>Detalle de una sesión de impersonación: cada consulta y cada escritura, en orden.</summary>
public sealed class ListImpersonationActionsHandler(IImpersonationSessionRepository sessions)
{
    public async Task<IReadOnlyList<ImpersonationActionItem>> HandleAsync(
        Guid sessionId, CancellationToken cancellationToken)
    {
        if (await sessions.GetByIdAsync(sessionId, cancellationToken) is null)
        {
            throw new NotFoundException("La sesión de impersonación no existe.");
        }

        var rows = await sessions.GetActionsAsync(sessionId, 1000, cancellationToken);
        return rows
            .Select(a => new ImpersonationActionItem(a.Id, a.Method, a.Endpoint, a.Summary, a.StatusCode, a.OccurredAt))
            .ToList();
    }
}
