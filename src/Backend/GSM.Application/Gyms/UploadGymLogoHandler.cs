using GSM.Application.Common;
using GSM.Application.Common.Abstractions;
using GSM.Application.Gyms.Abstractions;
using GSM.Application.Gyms.Contracts;
using GSM.Domain.Common.Exceptions;

namespace GSM.Application.Gyms;

public sealed class UploadGymLogoHandler(
    ICurrentUserService currentUser,
    IGymRepository gyms,
    IFileStorage storage,
    IUnitOfWork unitOfWork)
{
    public async Task<GymResponse> HandleAsync(FileToStore file, CancellationToken cancellationToken)
    {
        var gymId = currentUser.RequireGymId();
        ImageUploadRules.EnsureValid(file);

        var gym = await gyms.GetByIdAsync(gymId, cancellationToken)
            ?? throw new NotFoundException("El gym del usuario no existe.");

        var previous = gym.LogoUrl;
        var url = await storage.SaveAsync(file, "gym-logos", cancellationToken);

        gym.SetLogo(url);
        await unitOfWork.SaveChangesAsync(cancellationToken);

        if (!string.IsNullOrWhiteSpace(previous))
        {
            await storage.DeleteAsync(previous, cancellationToken);
        }

        return GymMapper.ToResponse(gym);
    }
}
