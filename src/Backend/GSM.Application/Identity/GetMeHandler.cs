using GSM.Application.Common.Abstractions;
using GSM.Application.Identity.Abstractions;
using GSM.Application.Identity.Contracts;
using GSM.Domain.Common.Exceptions;

namespace GSM.Application.Identity;

public sealed class GetMeHandler(
    ICurrentUserService currentUser,
    IUserCredentialsService credentials)
{
    public async Task<MeResponse> HandleAsync(CancellationToken cancellationToken)
    {
        var userId = currentUser.UserId
            ?? throw new UnauthorizedDomainException("No hay usuario autenticado en el contexto.");

        var user = await credentials.FindActiveByIdAsync(userId, cancellationToken)
            ?? throw new NotFoundException("El usuario del token ya no existe o está inactivo.");

        return new MeResponse(
            user.Id,
            user.FullName,
            user.Email,
            RoleName.ToClaim(user.Role),
            user.GymId);
    }
}
