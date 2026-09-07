using GSM.Application.Identity.Abstractions;
using GSM.Application.Identity.Contracts;
using GSM.Domain.Common.Exceptions;
using Microsoft.Extensions.Logging;

namespace GSM.Application.Identity;

public sealed class LoginHandler(
    IUserCredentialsService credentials,
    IJwtTokenService jwtTokens,
    IRefreshTokenService refreshTokens,
    ILogger<LoginHandler> logger)
{
    public async Task<AuthTokensResponse> HandleAsync(LoginRequest request, CancellationToken cancellationToken)
    {
        logger.LogInformation("Login solicitado para {Email}", request.Email);

        var user = await credentials.ValidateCredentialsAsync(request.Email, request.Password, cancellationToken)
            ?? throw new UnauthorizedDomainException("Credenciales inválidas.");

        var accessToken = jwtTokens.CreateAccessToken(user);
        var refreshToken = await refreshTokens.IssueAsync(user.Id, cancellationToken);

        logger.LogInformation("Login correcto para {UserId} con rol {Role}", user.Id, user.Role);

        return new AuthTokensResponse(accessToken.Value, refreshToken, accessToken.ExpiresAtUtc);
    }
}
