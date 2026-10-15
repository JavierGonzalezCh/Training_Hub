using GSM.Application.Common.Abstractions;
using GSM.Application.Common.Events;
using GSM.Application.Gyms.Abstractions;
using GSM.Application.Gyms.Contracts;
using GSM.Application.Identity.Events;
using GSM.Application.Identity.Public;
using GSM.Domain.Common.Exceptions;
using Microsoft.Extensions.Logging;

namespace GSM.Application.Gyms;

/// <summary>
/// Quién es el contacto de facturación del gym (Gyms §6): el Owner marcado para los cobros del SaaS y los avisos
/// de suscripción. Si nunca se eligió, o el elegido ya no es Owner activo, es el Owner activo más antiguo.
/// </summary>
public sealed class GetBillingContactHandler(
    ICurrentUserService currentUser,
    IGymRepository gyms,
    IIdentityReader identity)
{
    public Task<BillingContactResponse> HandleAsync(CancellationToken cancellationToken)
        => HandleAsync(currentUser.RequireGymId(), cancellationToken);

    public async Task<BillingContactResponse> HandleAsync(Guid gymId, CancellationToken cancellationToken)
    {
        var gym = await gyms.GetByIdAsync(gymId, cancellationToken)
            ?? throw new NotFoundException("El gym no existe.");

        var owners = await identity.ListActiveOwnersAsync(gymId, cancellationToken);
        var contact = owners.FirstOrDefault(o => o.UserId == gym.BillingContactUserId) ?? owners.FirstOrDefault();

        return contact is null
            ? new BillingContactResponse(null, null, null)
            : new BillingContactResponse(contact.UserId, contact.FullName, contact.Email);
    }
}

/// <summary>Marca a un Owner activo del gym como contacto de facturación. Solo un Owner (lo impone el controller).</summary>
public sealed class SetBillingContactHandler(
    ICurrentUserService currentUser,
    IGymRepository gyms,
    IIdentityReader identity,
    IUnitOfWork unitOfWork,
    ILogger<SetBillingContactHandler> logger)
{
    public async Task<BillingContactResponse> HandleAsync(
        SetBillingContactRequest request, CancellationToken cancellationToken)
    {
        var gymId = currentUser.RequireGymId();
        var gym = await gyms.GetByIdAsync(gymId, cancellationToken)
            ?? throw new NotFoundException("El gym del usuario no existe.");

        var owners = await identity.ListActiveOwnersAsync(gymId, cancellationToken);
        var contact = owners.FirstOrDefault(o => o.UserId == request.UserId)
            ?? throw new ValidationDomainException("El contacto de facturación debe ser un Owner activo del gimnasio.");

        gym.SetBillingContact(contact.UserId);
        await unitOfWork.SaveChangesAsync(cancellationToken);

        logger.LogInformation("Contacto de facturación del gym {GymId} fijado en {UserId}", gymId, contact.UserId);
        return new BillingContactResponse(contact.UserId, contact.FullName, contact.Email);
    }
}

/// <summary>
/// Si el Owner que se quitó era el contacto de facturación, pasa al Owner activo más antiguo que queda
/// (Gyms §6: "si se quita, pasa a otro Owner"). Reacciona a <see cref="GymOwnerRemoved"/>. Idempotente: si el
/// contacto ya no es él (o ya se reasignó), no hace nada.
/// </summary>
public sealed class ReassignBillingContactHandler(
    IGymRepository gyms,
    IIdentityReader identity,
    IUnitOfWork unitOfWork,
    ILogger<ReassignBillingContactHandler> logger) : IDomainEventHandler<GymOwnerRemoved>
{
    public async Task HandleAsync(GymOwnerRemoved domainEvent, CancellationToken cancellationToken)
    {
        var gym = await gyms.GetByIdAsync(domainEvent.GymId, cancellationToken);
        if (gym is null || gym.BillingContactUserId != domainEvent.UserId)
        {
            return;
        }

        var owners = await identity.ListActiveOwnersAsync(domainEvent.GymId, cancellationToken);
        var next = owners.FirstOrDefault(o => o.UserId != domainEvent.UserId);

        // Sin otro Owner activo (no debería pasar: no se quita al último) se deja como está.
        if (next is null)
        {
            return;
        }

        gym.SetBillingContact(next.UserId);
        await unitOfWork.SaveChangesAsync(cancellationToken);
        logger.LogInformation(
            "Contacto de facturación del gym {GymId} pasó de {OldUserId} a {NewUserId}",
            domainEvent.GymId, domainEvent.UserId, next.UserId);
    }
}
