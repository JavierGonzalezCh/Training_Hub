using GSM.Application.Identity.Abstractions;
using GSM.Application.Identity.Contracts;
using GSM.Domain.Common.Exceptions;
using Microsoft.Extensions.Logging;

namespace GSM.Application.Identity;

public sealed class RefreshTokenHandler(
    IUserCredentialsService credentials,
    IJwtTokenService jwtTokens,
    IRefreshTokenService refreshTokens,
    ILogger<RefreshTokenHandler> logger)
{
    public async Task<AuthTokensResponse> HandleAsync(RefreshRequest request, CancellationToken cancellationToken)
    {
        logger.LogInformation("Refresh de token solicitado");

        var userId = await refreshTokens.ResolveActiveUserAsync(request.RefreshToken, cancellationToken)
            ?? throw new UnauthorizedDomainException("Refresh token inválido o expirado.");

        var user = await credentials.FindActiveByIdAsync(userId, cancellationToken)
            ?? throw new UnauthorizedDomainException("La cuenta no está disponible.");

        // Rotación: el token usado se revoca y se emite uno nuevo.
        await refreshTokens.RevokeAsync(request.RefreshToken, cancellationToken);

        var accessToken = jwtTokens.CreateAccessToken(user);
        var newRefreshToken = await refreshTokens.IssueAsync(user.Id, cancellationToken);

        logger.LogInformation("Refresh correcto para {UserId}", user.Id);

        return new AuthTokensResponse(accessToken.Value, newRefreshToken, accessToken.ExpiresAtUtc);
    }
}
