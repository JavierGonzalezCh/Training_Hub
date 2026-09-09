using GSM.Application.Common.Abstractions;
using GSM.Application.SuperAdmin.Abstractions;
using GSM.Domain.Common.Exceptions;
using Microsoft.Extensions.Logging;

namespace GSM.Application.SuperAdmin;

/// <summary>
/// Cierra una sesión de impersonación. La identifica el claim del token si se llama con él, o la
/// sesión abierta del superadmin en caso contrario.
/// </summary>
public sealed class EndImpersonationHandler(
    IClock clock,
    ICurrentUserService currentUser,
    IImpersonationSessionRepository sessions,
    IUnitOfWork unitOfWork,
    ILogger<EndImpersonationHandler> logger)
{
    public async Task HandleAsync(CancellationToken cancellationToken)
    {
        var superAdminId = currentUser.UserId
            ?? throw new UnauthorizedDomainException("No hay usuario en el contexto.");

        var session = currentUser.ImpersonationSessionId is { } sessionId
            ? await sessions.GetByIdAsync(sessionId, cancellationToken)
            : await sessions.GetActiveForUserAsync(superAdminId, cancellationToken);

        if (session is null)
        {
            throw new NotFoundException("No hay una sesión de impersonación activa.");
        }

        if (session.SuperAdminUserId != superAdminId)
        {
            throw new ForbiddenDomainException("Esa sesión de impersonación es de otro superadmin.");
        }

        session.End(clock.UtcNow);
        await unitOfWork.SaveChangesAsync(cancellationToken);

        logger.LogInformation("Impersonación {SessionId} cerrada por superadmin {UserId}", session.Id, superAdminId);
    }
}
