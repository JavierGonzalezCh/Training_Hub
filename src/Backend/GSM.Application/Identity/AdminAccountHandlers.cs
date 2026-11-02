using GSM.Application.Common;
using GSM.Application.Common.Abstractions;
using GSM.Application.Identity.Abstractions;
using GSM.Application.Identity.Contracts;
using GSM.Domain.Common.Exceptions;
using Microsoft.Extensions.Logging;

namespace GSM.Application.Identity;

/// <summary>Las cuentas de un gym (owners, staff y miembros) en la ficha del SuperAdmin.</summary>
public sealed class ListGymAccountsHandler(IAdminAccountService accounts)
{
    private const int MaxPageSize = 100;

    public async Task<PagedResult<GymAccountResponse>> HandleAsync(
        Guid gymId, string? search, int page, int pageSize, CancellationToken cancellationToken)
    {
        page = Math.Max(page, 1);
        pageSize = Math.Clamp(pageSize, 1, MaxPageSize);

        var result = await accounts.ListAsync(gymId, search, page, pageSize, cancellationToken);
        var items = result.Items
            .Select(a => new GymAccountResponse(
                a.UserId, a.Email, a.FullName, a.Role.ToString().ToLowerInvariant(), a.IsActive, a.HasPassword))
            .ToList();
        return new PagedResult<GymAccountResponse>(items, result.Page, result.PageSize, result.Total);
    }
}

/// <summary>
/// "Cambiar clave" del SuperAdmin (solo él, lo impone el controller): fija la contraseña de una persona del gym
/// sin pasar por el correo y cierra todas sus sesiones.
/// </summary>
public sealed class AdminSetPasswordHandler(
    ICurrentUserService currentUser,
    IAdminAccountService accounts,
    ILogger<AdminSetPasswordHandler> logger)
{
    public async Task HandleAsync(
        Guid gymId, Guid userId, AdminSetPasswordRequest request, CancellationToken cancellationToken)
    {
        var actorId = currentUser.UserId
            ?? throw new UnauthorizedDomainException("No hay usuario autenticado en el contexto.");

        await accounts.SetPasswordAsync(gymId, userId, request.NewPassword, actorId, cancellationToken);
        logger.LogInformation(
            "Contraseña de {UserId} fijada por el SuperAdmin {ActorId} desde el gym {GymId}", userId, actorId, gymId);
    }
}
