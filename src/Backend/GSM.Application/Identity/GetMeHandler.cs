using GSM.Application.Common.Abstractions;
using GSM.Application.Identity.Abstractions;
using GSM.Application.Identity.Contracts;
using GSM.Domain.Common.Exceptions;

namespace GSM.Application.Identity;

public sealed class GetMeHandler(
    ICurrentUserService currentUser,
    IUserCredentialsService credentials,
    IGymAccessService accesses)
{
    public async Task<MeResponse> HandleAsync(CancellationToken cancellationToken)
    {
        var userId = currentUser.UserId
            ?? throw new UnauthorizedDomainException("No hay usuario autenticado en el contexto.");

        var account = await credentials.FindActiveByIdAsync(userId, cancellationToken)
            ?? throw new NotFoundException("El usuario del token ya no existe o está inactivo.");

        // El SuperAdmin (también impersonando) no tiene gym propio.
        if (account.IsSuperAdmin)
        {
            return new MeResponse(account.Id, account.FullName, account.Email, RoleName.SuperAdmin, null);
        }

        // Gym y rol salen del token (el acceso con el que se entró), pero el acceso debe seguir activo.
        var gymId = currentUser.GymId
            ?? throw new UnauthorizedDomainException("El token no trae gym.");
        var role = currentUser.Role
            ?? throw new UnauthorizedDomainException("El token no trae rol.");

        _ = await accesses.FindActiveAsync(userId, gymId, role, cancellationToken)
            ?? throw new NotFoundException("El acceso al gimnasio ya no existe o está inactivo.");

        return new MeResponse(account.Id, account.FullName, account.Email, RoleName.ToClaim(role), gymId);
    }
}
