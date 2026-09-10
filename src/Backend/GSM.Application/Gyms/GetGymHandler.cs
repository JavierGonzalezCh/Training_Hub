using GSM.Application.Common.Abstractions;
using GSM.Application.Gyms.Abstractions;
using GSM.Application.Gyms.Contracts;
using GSM.Domain.Common.Exceptions;

namespace GSM.Application.Gyms;

/// <summary>
/// Datos del gym del usuario. El bloque <c>subscription</c> lo añade el controller desde SuperAdmin:
/// así Gyms no depende de ese módulo (§4.5 de la especificación técnica).
/// </summary>
public sealed class GetGymHandler(
    ICurrentUserService currentUser,
    IGymRepository gyms)
{
    public async Task<GymResponse> HandleAsync(CancellationToken cancellationToken)
    {
        var gymId = currentUser.RequireGymId();
        var gym = await gyms.GetByIdAsync(gymId, cancellationToken)
            ?? throw new NotFoundException("El gym del usuario no existe.");

        return GymMapper.ToResponse(gym);
    }
}
