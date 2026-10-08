using GSM.Application.Common.Abstractions;
using GSM.Application.Gyms.Abstractions;
using GSM.Application.Gyms.Contracts;
using GSM.Domain.Common.Exceptions;
using Microsoft.Extensions.Logging;

namespace GSM.Application.Gyms;

public sealed class UpdateGymHandler(
    ICurrentUserService currentUser,
    IGymRepository gyms,
    IUnitOfWork unitOfWork,
    ILogger<UpdateGymHandler> logger)
{
    public async Task<GymResponse> HandleAsync(UpdateGymRequest request, CancellationToken cancellationToken)
    {
        var gymId = currentUser.RequireGymId();
        var gym = await gyms.GetByIdAsync(gymId, cancellationToken)
            ?? throw new NotFoundException("El gym del usuario no existe.");

        gym.UpdateProfile(request.Name, request.Nit, request.Phone, request.Email, request.LegalName);
        gym.UpdateLocation(
            request.Address, request.City, request.Latitude, request.Longitude, request.GeofenceRadiusMeters);
        gym.UpdateSocial(
            request.InstagramUrl, request.FacebookUrl, request.TikTokUrl, request.WebsiteUrl, request.WhatsAppPhone);
        gym.UpdateCapacity(request.Capacity);
        gym.UpdateBranding(request.LogoUrl, request.PrimaryColor, request.SecondaryColor);

        await unitOfWork.SaveChangesAsync(cancellationToken);
        logger.LogInformation("Gym {GymId} actualizado", gymId);

        return GymMapper.ToResponse(gym);
    }
}
