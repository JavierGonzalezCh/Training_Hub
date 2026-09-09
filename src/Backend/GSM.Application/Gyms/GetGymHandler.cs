using GSM.Application.Common.Abstractions;
using GSM.Application.Gyms.Abstractions;
using GSM.Application.Gyms.Contracts;
using GSM.Application.SuperAdmin.Public;
using GSM.Domain.Common.Exceptions;

namespace GSM.Application.Gyms;

public sealed class GetGymHandler(
    ICurrentUserService currentUser,
    IGymRepository gyms,
    ISubscriptionReader subscriptions)
{
    public async Task<GymResponse> HandleAsync(CancellationToken cancellationToken)
    {
        var gymId = currentUser.RequireGymId();
        var gym = await gyms.GetByIdAsync(gymId, cancellationToken)
            ?? throw new NotFoundException("El gym del usuario no existe.");

        var subscription = SubscriptionInfo.From(
            await subscriptions.GetStatusViewAsync(gymId, cancellationToken));

        return GymMapper.ToResponse(gym) with { Subscription = subscription };
    }
}
