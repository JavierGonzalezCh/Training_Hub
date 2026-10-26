using GSM.Application.Common.Abstractions;
using GSM.Application.Gyms.Abstractions;
using GSM.Application.Gyms.Contracts;
using GSM.Domain.Common.Exceptions;
using Microsoft.Extensions.Logging;

namespace GSM.Application.Gyms;

/// <summary>
/// El SuperAdmin cambia el tipo de negocio de un gym. Solo cambia la etiqueta y lo que depende de ella (métricas,
/// sugerencias del onboarding): ninguna configuración se reaplica, para no pisar lo que el dueño ya ajustó.
/// </summary>
public sealed class ChangeGymTypeHandler(
    IGymRepository gyms,
    IUnitOfWork unitOfWork,
    ILogger<ChangeGymTypeHandler> logger)
{
    public async Task<GymResponse> HandleAsync(Guid gymId, ChangeGymTypeRequest request, CancellationToken cancellationToken)
    {
        var gym = await gyms.GetByIdAsync(gymId, cancellationToken)
            ?? throw new NotFoundException("El gym no existe.");

        var before = gym.Type;
        gym.ChangeType(request.Type);
        await unitOfWork.SaveChangesAsync(cancellationToken);

        logger.LogInformation("Tipo del gym {GymId} cambiado de {Before} a {After}", gymId, before, gym.Type);
        return GymMapper.ToResponse(gym);
    }
}
