using GSM.Application.Common.Abstractions;
using GSM.Application.Common.Email;
using GSM.Application.Common.Events;
using GSM.Application.Gyms.Public;
using GSM.Application.Identity.Abstractions;
using GSM.Application.Identity.Contracts;
using GSM.Application.Identity.Events;
using GSM.Domain.Common.Exceptions;
using GSM.Domain.Identity;
using Microsoft.Extensions.Logging;

namespace GSM.Application.Identity;

/// <summary>Los Owners del gym del usuario (Configuración → Equipo).</summary>
public sealed class ListMyOwnersHandler(ICurrentUserService currentUser, IAccountLinkService links)
{
    public async Task<IReadOnlyList<GymOwnerResponse>> HandleAsync(CancellationToken cancellationToken)
        => (await links.ListOwnersAsync(currentUser.RequireGymId(), cancellationToken))
            .Select(o => new GymOwnerResponse(o.UserId, o.Email, o.FullName, o.IsActive, o.HasPassword))
            .ToList();
}

/// <summary>
/// Agrega un Owner: lo invita por email (nace sin contraseña, enlace de activación de 72 h). Lo usa el Owner con
/// su propio gym y el SuperAdmin con el gym que indique ("el dueño perdió el acceso", Gyms §6).
/// </summary>
public sealed class InviteOwnerHandler(
    ICurrentUserService currentUser,
    IOwnerAccountService owners,
    IGymReader gyms,
    IEmailSender emailSender,
    ILogger<InviteOwnerHandler> logger)
{
    public Task<GymOwnerResponse> HandleAsync(InviteOwnerRequest request, CancellationToken cancellationToken)
        => HandleAsync(currentUser.RequireGymId(), request, cancellationToken);

    public async Task<GymOwnerResponse> HandleAsync(
        Guid gymId, InviteOwnerRequest request, CancellationToken cancellationToken)
    {
        var actorId = currentUser.UserId
            ?? throw new UnauthorizedDomainException("No hay usuario autenticado en el contexto.");
        var gym = await gyms.GetAdminViewAsync(gymId, cancellationToken)
            ?? throw new NotFoundException("El gym no existe.");

        var email = request.Email.Trim();
        var name = request.FullName.Trim();
        var result = await owners.InviteAsync(gymId, email, name, actorId, cancellationToken);

        // La cuenta ya existe: si el correo falla, el enlace se copia con "Copiar enlace".
        try
        {
            await emailSender.SendAsync(
                result.HadAccount
                    ? EmailTemplates.OwnerAccessGranted(result.Owner.Email, result.Owner.FullName, gym.Name, result.ActivationUrl)
                    : EmailTemplates.OwnerInvitation(result.Owner.Email, result.Owner.FullName, gym.Name, result.ActivationUrl),
                cancellationToken);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "No se pudo enviar la invitación al owner {UserId}", result.Owner.UserId);
        }

        logger.LogInformation("Owner {UserId} agregado al gym {GymId} por {ActorId}", result.Owner.UserId, gymId, actorId);
        return ToResponse(result.Owner);
    }

    internal static GymOwnerResponse ToResponse(OwnerAccount o)
        => new(o.UserId, o.Email, o.FullName, o.IsActive, o.HasPassword);
}

/// <summary>Asciende a un staff del gym a Owner (solo un Owner, lo impone el controller).</summary>
public sealed class PromoteStaffToOwnerHandler(
    ICurrentUserService currentUser,
    IOwnerAccountService owners,
    ILogger<PromoteStaffToOwnerHandler> logger)
{
    public async Task<GymOwnerResponse> HandleAsync(Guid userId, CancellationToken cancellationToken)
    {
        var gymId = currentUser.RequireGymId();
        var actorId = currentUser.UserId
            ?? throw new UnauthorizedDomainException("No hay usuario autenticado en el contexto.");

        var owner = await owners.PromoteStaffAsync(gymId, userId, actorId, cancellationToken);
        logger.LogInformation("Staff {UserId} ascendido a Owner en el gym {GymId} por {ActorId}", userId, gymId, actorId);
        return InviteOwnerHandler.ToResponse(owner);
    }
}

/// <summary>
/// Quita a un Owner (a otro o a sí mismo; el SuperAdmin, a cualquiera del gym que indique). No se puede quitar
/// al último. Publica <see cref="GymOwnerRemoved"/> (Gyms reasigna el contacto de facturación) y
/// <see cref="StaffTrainerRoleLost"/> (ya no es entrenador asignable: ni staff de Recepción tiene <c>Trainer</c>).
/// </summary>
public sealed class RemoveOwnerHandler(
    ICurrentUserService currentUser,
    IOwnerAccountService owners,
    IDomainEventPublisher events,
    ILogger<RemoveOwnerHandler> logger)
{
    public Task<GymOwnerResponse> HandleAsync(
        Guid userId, bool keepAsStaff, string? staffProfile, CancellationToken cancellationToken)
        => HandleAsync(currentUser.RequireGymId(), userId, keepAsStaff, staffProfile, cancellationToken);

    public async Task<GymOwnerResponse> HandleAsync(
        Guid gymId, Guid userId, bool keepAsStaff, string? staffProfile, CancellationToken cancellationToken)
    {
        var actorId = currentUser.UserId
            ?? throw new UnauthorizedDomainException("No hay usuario autenticado en el contexto.");

        // Sin perfil indicado, el de menos privilegio (Recepción); el Owner lo ajusta después.
        var profile = StaffProfile.Reception;
        if (staffProfile is not null && !PermissionName.TryParseProfile(staffProfile, out profile))
        {
            throw new ValidationDomainException("Perfil desconocido. Usa reception, trainer o administrator.");
        }

        var owner = await owners.RemoveAsync(gymId, userId, keepAsStaff, profile, actorId, cancellationToken);

        await events.PublishAsync(new GymOwnerRemoved(gymId, userId), cancellationToken);
        await events.PublishAsync(new StaffTrainerRoleLost(gymId, userId), cancellationToken);

        logger.LogInformation(
            "Owner {UserId} quitado del gym {GymId} por {ActorId} (queda como staff: {KeepAsStaff})",
            userId, gymId, actorId, keepAsStaff);
        return InviteOwnerHandler.ToResponse(owner);
    }
}
