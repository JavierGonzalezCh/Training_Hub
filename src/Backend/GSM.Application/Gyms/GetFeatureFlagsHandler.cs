using GSM.Application.Common.Abstractions;
using GSM.Application.Gyms.Abstractions;
using GSM.Application.Gyms.Contracts;
using GSM.Domain.Common.Exceptions;

namespace GSM.Application.Gyms;

public sealed class GetFeatureFlagsHandler(ICurrentUserService currentUser, IGymRepository gyms)
{
    public async Task<GymFeatureFlagsResponse> HandleAsync(CancellationToken cancellationToken)
    {
        var gymId = currentUser.RequireGymId();
        var flags = await gyms.GetFeatureFlagsAsync(gymId, cancellationToken)
            ?? throw new NotFoundException("El gym del usuario no tiene feature flags.");

        return GymMapper.ToResponse(flags);
    }
}
