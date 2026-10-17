using GSM.Application.Common.Abstractions;
using GSM.Application.Common.Email;
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

/// <summary>
/// Alta de un gym por el superadmin: gym + feature flags por defecto + owner inicial, en una transacción.
/// El Owner nace <b>sin contraseña</b> y recibe un enlace de activación por correo (Gyms §4). Con prueba, el gym
/// nace en <c>trial</c>; sin prueba, nace <c>active</c> y el primer pago se registra en su suscripción (SuperAdmin §V2).
/// </summary>
public sealed class CreateGymHandler(
    IGymRepository gyms,
    IUserProvisioning userProvisioning,
    IUnitOfWork unitOfWork,
    IDomainEventPublisher events,
    IClock clock,
    ICurrentUserService currentUser,
    IEmailSender emailSender,
    ILogger<CreateGymHandler> logger)
{
    public Task<CreateGymResponse> HandleAsync(CreateGymRequest request, CancellationToken cancellationToken)
        => CreateAsync(request, ownerPassword: null, cancellationToken);

    /// <summary>
    /// Solo para el sembrado de desarrollo y las pruebas: el Owner nace con contraseña, sin invitación. No hay
    /// endpoint que la use; la API siempre invita por enlace.
    /// </summary>
    public Task<CreateGymResponse> HandleWithOwnerPasswordAsync(
        CreateGymRequest request, string ownerPassword, CancellationToken cancellationToken)
        => CreateAsync(request, ownerPassword, cancellationToken);

    private async Task<CreateGymResponse> CreateAsync(
        CreateGymRequest request, string? ownerPassword, CancellationToken cancellationToken)
    {
        var slug = request.Slug.Trim().ToLowerInvariant();
        logger.LogInformation("Alta de gym solicitada: slug {Slug}", slug);

        var hasTrial = request.TrialDays is not null;
        if (hasTrial == (request.FirstPayment is not null))
        {
            throw new ValidationDomainException(
                "Elige una: o el gym tiene prueba (con sus días) o se registra su primer pago.");
        }

        var firstPayment = ResolveFirstPayment(request.FirstPayment);

        if (await gyms.SlugExistsAsync(slug, cancellationToken))
        {
            throw new ConflictException($"Ya existe un gym con el slug '{slug}'.");
        }

        Guid? createdBy = null;
        if (firstPayment is not null)
        {
            createdBy = currentUser.UserId
                ?? throw new UnauthorizedDomainException("No hay usuario autenticado en el contexto.");
        }

        var gym = Gym.Create(
            request.Name, slug, request.Nit, request.Phone, request.Email);
        if (!hasTrial)
        {
            // Sin prueba el gym nace activo (SuperAdmin §V2).
            gym.SetCommercialStatus(GymStatus.Active);
        }

        var featureFlags = GymFeatureFlags.Default(gym.Id);

        OwnerInvitation? invitation = null;
        var ownerId = Guid.Empty;

        await unitOfWork.ExecuteInTransactionAsync(async innerToken =>
        {
            gyms.Add(gym);
            gyms.Add(featureFlags);

            if (ownerPassword is null)
            {
                invitation = await userProvisioning.InviteGymOwnerAsync(
                    request.Owner.Email.Trim(), request.Owner.FullName.Trim(), gym.Id, innerToken);
                ownerId = invitation.UserId;
            }
            else
            {
                ownerId = await userProvisioning.CreateGymUserAsync(
                    request.Owner.Email,
                    request.Owner.FullName,
                    ownerPassword,
                    UserRole.Owner,
                    gym.Id,
                    innerToken);
            }

            gym.AssignOwner(ownerId);
            gym.SetBillingContact(ownerId);

            await unitOfWork.SaveChangesAsync(innerToken);
        }, cancellationToken);

        logger.LogInformation("Gym {GymId} creado con owner {OwnerId}", gym.Id, ownerId);

        await events.PublishAsync(
            new GymCreated(
                gym.Id, gym.Name, request.SaasPlanId, request.TrialDays, firstPayment, request.MonthlyPrice ?? 0m, createdBy),
            cancellationToken);

        var sent = invitation is null || await SendInvitationAsync(request.Owner, gym.Name, invitation, cancellationToken);
        return new CreateGymResponse(gym.Id, ownerId, sent);
    }

    // La cuenta ya existe: si el correo falla, el SuperAdmin copia el enlace desde la ficha del gym.
    private async Task<bool> SendInvitationAsync(
        CreateGymOwner owner, string gymName, OwnerInvitation invitation, CancellationToken cancellationToken)
    {
        try
        {
            var email = owner.Email.Trim();
            var name = owner.FullName.Trim();
            await emailSender.SendAsync(
                invitation.HadAccount
                    ? EmailTemplates.OwnerAccessGranted(email, name, gymName, invitation.ActivationUrl)
                    : EmailTemplates.OwnerInvitation(email, name, gymName, invitation.ActivationUrl),
                cancellationToken);
            return true;
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "No se pudo enviar la invitación al owner {UserId}", invitation.UserId);
            return false;
        }
    }

    private GymFirstPayment? ResolveFirstPayment(CreateGymFirstPayment? payment)
    {
        if (payment is null)
        {
            return null;
        }

        if (payment.Amount <= 0)
        {
            throw new ValidationDomainException("El monto del primer pago debe ser mayor que cero.");
        }

        var today = clock.Today;
        var from = payment.CoversFrom ?? today;
        var through = payment.CoversThrough ?? from.AddMonths(1).AddDays(-1);

        if (through < from)
        {
            throw new ValidationDomainException("El periodo cubierto no puede terminar antes de empezar.");
        }

        if (through < today)
        {
            throw new ValidationDomainException(
                "El primer pago debe cubrir hasta hoy o después: el gym nace activo.");
        }

        return new GymFirstPayment(payment.Amount, payment.Method, from, through, payment.Reference, payment.Notes);
    }
}
