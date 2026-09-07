using GSM.Application.Identity.Abstractions;
using GSM.Application.Identity.Contracts;
using Microsoft.Extensions.Logging;

namespace GSM.Application.Identity;

public sealed class LogoutHandler(
    IRefreshTokenService refreshTokens,
    ILogger<LogoutHandler> logger)
{
    public async Task HandleAsync(LogoutRequest request, CancellationToken cancellationToken)
    {
        await refreshTokens.RevokeAsync(request.RefreshToken, cancellationToken);
        logger.LogInformation("Logout: refresh token revocado");
    }
}
