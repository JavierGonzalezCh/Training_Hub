using GSM.Application.Common.Abstractions;
using GSM.Application.Common.Events;
using GSM.Application.Gyms.Abstractions;
using GSM.Application.Gyms.Contracts;
using GSM.Application.Gyms.Events;
using GSM.Application.Identity.Public;
using GSM.Domain.Common.Exceptions;
using GSM.Domain.Gyms;
using GSM.Domain.Identity;
using Microsoft.Extensions.Logging;

namespace GSM.Application.Gyms;

/// <summary>Alta de un gym por el superadmin: gym + feature flags por defecto + owner inicial, en una transacción.</summary>
public sealed class CreateGymHandler(
    IGymRepository gyms,
    IUserProvisioning userProvisioning,
    IUnitOfWork unitOfWork,
    IDomainEventPublisher events,
    ILogger<CreateGymHandler> logger)
{
    public async Task<CreateGymResponse> HandleAsync(CreateGymRequest request, CancellationToken cancellationToken)
    {
        var slug = request.Slug.Trim().ToLowerInvariant();
        logger.LogInformation("Alta de gym solicitada: slug {Slug}", slug);

        if (await gyms.SlugExistsAsync(slug, cancellationToken))
        {
            throw new ConflictException($"Ya existe un gym con el slug '{slug}'.");
        }

        var gym = Gym.Create(
            request.Name, slug, request.Plan, request.Nit, request.Phone, request.Email);
        var featureFlags = GymFeatureFlags.Default(gym.Id);

        var ownerId = Guid.Empty;

        await unitOfWork.ExecuteInTransactionAsync(async innerToken =>
        {
            gyms.Add(gym);
            gyms.Add(featureFlags);

            ownerId = await userProvisioning.CreateGymUserAsync(
                request.Owner.Email,
                request.Owner.FullName,
                request.Owner.Password,
                UserRole.Owner,
                gym.Id,
                innerToken);

            gym.AssignOwner(ownerId);

            await unitOfWork.SaveChangesAsync(innerToken);
        }, cancellationToken);

        logger.LogInformation("Gym {GymId} creado con owner {OwnerId}", gym.Id, ownerId);

        await events.PublishAsync(new GymCreated(gym.Id, gym.Name), cancellationToken);

        return new CreateGymResponse(gym.Id, ownerId);
    }
}
