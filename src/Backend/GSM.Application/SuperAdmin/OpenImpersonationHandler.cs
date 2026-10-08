using GSM.Application.Common.Abstractions;
using GSM.Application.Gyms.Public;
using GSM.Application.Identity.Abstractions;
using GSM.Application.SuperAdmin.Abstractions;
using GSM.Application.SuperAdmin.Contracts;
using GSM.Domain.Common.Exceptions;
using GSM.Domain.SuperAdmin;
using Microsoft.Extensions.Logging;

namespace GSM.Application.SuperAdmin;

/// <summary>
/// Abre una sesión de impersonación (motivo obligatorio) y emite un token de vida corta que
/// mantiene la identidad real del superadmin y lo acota al gym visitado, en solo lectura.
/// </summary>
public sealed class OpenImpersonationHandler(
    IClock clock,
    ICurrentUserService currentUser,
    IGymReader gyms,
    IUserCredentialsService credentials,
    IJwtTokenService jwtTokens,
    IImpersonationSessionRepository sessions,
    IUnitOfWork unitOfWork,
    ILogger<OpenImpersonationHandler> logger)
{
    public async Task<ImpersonationTokenResponse> HandleAsync(
        Guid gymId, OpenImpersonationRequest request, CancellationToken cancellationToken)
    {
        var superAdminId = currentUser.UserId
            ?? throw new UnauthorizedDomainException("No hay usuario en el contexto.");

        if (currentUser.IsImpersonating)
        {
            throw new ConflictException("Ya hay una impersonación en curso; ciérrala antes de abrir otra.");
        }

        var superAdmin = await credentials.FindActiveByIdAsync(superAdminId, cancellationToken)
            ?? throw new UnauthorizedDomainException("El superadmin ya no existe o está inactivo.");

        if (!await gyms.ExistsAsync(gymId, cancellationToken))
        {
            throw new NotFoundException("El gym no existe.");
        }

        if (!ImpersonationModeName.TryParse(request.Mode, out var mode))
        {
            throw new ValidationDomainException("El modo de la impersonación debe ser \"read\" o \"write\".");
        }

        var session = ImpersonationSession.Open(superAdminId, gymId, request.Reason, mode, clock.UtcNow);
        sessions.Add(session);
        await unitOfWork.SaveChangesAsync(cancellationToken);

        var token = jwtTokens.CreateImpersonationToken(
            new ImpersonationTokenRequest(
                superAdminId, superAdmin.FullName, gymId, session.Id, mode == ImpersonationMode.Write));

        logger.LogInformation(
            "Impersonación {SessionId} ({Mode}) abierta: superadmin {UserId} → gym {GymId}. Motivo: {Reason}",
            session.Id, ImpersonationModeName.ToClaim(mode), superAdminId, gymId, session.Reason);

        return new ImpersonationTokenResponse(
            token.Value, token.ExpiresAtUtc, session.Id, gymId, ImpersonationModeName.ToClaim(mode));
    }
}
