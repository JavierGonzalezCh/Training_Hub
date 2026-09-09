using GSM.Application.Identity.Abstractions;
using GSM.Application.Identity.Contracts;
using GSM.Application.SuperAdmin.Public;
using GSM.Domain.Common.Exceptions;
using GSM.Domain.SuperAdmin;
using Microsoft.Extensions.Logging;

namespace GSM.Application.Identity;

public sealed class LoginHandler(
    IUserCredentialsService credentials,
    IJwtTokenService jwtTokens,
    IRefreshTokenService refreshTokens,
    ISubscriptionReader subscriptions,
    ILogger<LoginHandler> logger)
{
    public async Task<AuthTokensResponse> HandleAsync(LoginRequest request, CancellationToken cancellationToken)
    {
        logger.LogInformation("Login solicitado para {Email}", request.Email);

        var user = await credentials.ValidateCredentialsAsync(request.Email, request.Password, cancellationToken)
            ?? throw new UnauthorizedDomainException("Credenciales inválidas.");

        // El bloqueo por estado vive en un filtro de la API, pero el login es anónimo y el filtro
        // no puede cubrirlo: un gym `suspended` no deja entrar ni a sus usuarios.
        if (user.GymId is { } gymId
            && await subscriptions.GetEffectiveStatusAsync(gymId, cancellationToken)
                == EffectiveSubscriptionStatus.Suspended)
        {
            logger.LogWarning("Login rechazado para {UserId}: gym {GymId} suspendido", user.Id, gymId);
            throw new ForbiddenDomainException("La cuenta del gimnasio está suspendida. Contacta con soporte.");
        }

        var accessToken = jwtTokens.CreateAccessToken(user);
        var refreshToken = await refreshTokens.IssueAsync(user.Id, cancellationToken);

        logger.LogInformation("Login correcto para {UserId} con rol {Role}", user.Id, user.Role);

        return new AuthTokensResponse(accessToken.Value, refreshToken, accessToken.ExpiresAtUtc);
    }
}
