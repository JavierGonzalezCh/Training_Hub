using GSM.Application.Common.Abstractions;
using GSM.Application.Gyms.Abstractions;
using GSM.Application.Gyms.Contracts;
using GSM.Domain.Common.Exceptions;
using Microsoft.Extensions.Logging;

namespace GSM.Application.Gyms;

public sealed class UpdateFeatureFlagsHandler(
    ICurrentUserService currentUser,
    IGymRepository gyms,
    IUnitOfWork unitOfWork,
    ILogger<UpdateFeatureFlagsHandler> logger)
{
    public async Task<GymFeatureFlagsResponse> HandleAsync(
        UpdateFeatureFlagsRequest request, CancellationToken cancellationToken)
    {
        var gymId = currentUser.RequireGymId();
        var flags = await gyms.GetFeatureFlagsAsync(gymId, cancellationToken)
            ?? throw new NotFoundException("El gym del usuario no tiene feature flags.");

        flags.Update(
            request.ShowRoutines,
            request.ShowClasses,
            request.ShowProgress,
            request.ShowGamification,
            request.ShowCommunity,
            request.CheckinQr,
            request.CommunityComments,
            request.MembersCanCreateRoutines,
            request.MembersCanReportEquipment);

        await unitOfWork.SaveChangesAsync(cancellationToken);
        logger.LogInformation("Feature flags del gym {GymId} actualizados", gymId);

        return GymMapper.ToResponse(flags);
    }
}
