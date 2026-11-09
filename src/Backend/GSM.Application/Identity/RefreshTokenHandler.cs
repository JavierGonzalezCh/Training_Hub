using GSM.Application.Identity.Abstractions;
using GSM.Application.Common.Logs;
using GSM.Application.Identity.Contracts;
using GSM.Domain.Common.Exceptions;
using Microsoft.Extensions.Logging;

namespace GSM.Application.Identity;

public sealed class RefreshTokenHandler(
    IUserCredentialsService credentials,
    IGymAccessService accesses,
    IRefreshTokenService refreshTokens,
    SessionIssuer sessions,
    ILogStore logStore,
    ILogger<RefreshTokenHandler> logger)
{
    public async Task<AuthTokensResponse> HandleAsync(RefreshRequest request, CancellationToken cancellationToken)
    {
        logger.LogInformation("Refresh de token solicitado");

        var owner = await refreshTokens.ResolveActiveAsync(request.RefreshToken, cancellationToken);
        if (owner is null)
        {
            await Rejected(await refreshTokens.DescribeInactiveAsync(request.RefreshToken, cancellationToken));
            throw new UnauthorizedDomainException("Refresh token inválido o expirado.");
        }

        var account = await credentials.FindActiveByIdAsync(owner.UserId, cancellationToken);
        if (account is null)
        {
            await Rejected("account_inactive");
            throw new UnauthorizedDomainException("La cuenta no está disponible.");
        }

        // La renovación respeta el acceso para el que se emitió la sesión: mismo gym y mismo rol.
        // Un acceso desactivado entre tanto corta la renovación.
        Func<Task<AuthTokensResponse>> issue;
        if (owner.GymAccessId is { } accessId)
        {
            var access = await accesses.GetActiveAsync(account.Id, accessId, cancellationToken);
            if (access is null)
            {
                await Rejected("access_inactive");
                throw new UnauthorizedDomainException("El acceso al gimnasio ya no está disponible.");
            }

            issue = () => sessions.IssueForAccessAsync(account, access, cancellationToken);
        }
        else if (account.IsSuperAdmin)
        {
            issue = () => sessions.IssueForSuperAdminAsync(account, cancellationToken);
        }
        else
        {
            await Rejected("invalid");
            throw new UnauthorizedDomainException("Refresh token inválido o expirado.");
        }

        // Rotación: el token usado se revoca y se emite uno nuevo.
        await refreshTokens.RevokeAsync(request.RefreshToken, cancellationToken);
        var tokens = await issue();

        logger.LogInformation("Refresh correcto para {UserId}", account.Id);
        return tokens;
    }

    // E5: cada renovación rechazada queda en logs.security_events, con el motivo y sin el token.
    private Task Rejected(string reason)
    {
        logStore.RecordSecurityEvent(new SecurityEventLogEntry(SecurityEventTypes.RefreshFailed, reason));
        return Task.CompletedTask;
    }
}
