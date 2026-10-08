using GSM.Application.Common.Abstractions;
using GSM.Application.Common.Email;
using GSM.Application.Identity.Abstractions;
using GSM.Application.Identity.Contracts;
using GSM.Domain.Common.Exceptions;
using Microsoft.Extensions.Logging;

namespace GSM.Application.Identity;

public sealed class ListStaffHandler(ICurrentUserService currentUser, IStaffAccountService staff)
{
    public async Task<IReadOnlyList<StaffResponse>> HandleAsync(CancellationToken cancellationToken)
    {
        var accounts = await staff.ListAsync(currentUser.RequireGymId(), cancellationToken);
        return accounts.Select(StaffMapping.ToResponse).ToList();
    }
}

/// <summary>
/// Alta de staff: la cuenta nace sin contraseña y el usuario recibe por email un código para
/// definirla en la pantalla de restablecimiento (el mismo flujo de "olvidé mi contraseña").
/// </summary>
public sealed class CreateStaffHandler(
    ICurrentUserService currentUser,
    IStaffAccountService staff,
    IEmailSender emailSender,
    ILogger<CreateStaffHandler> logger)
{
    public async Task<StaffResponse> HandleAsync(CreateStaffRequest request, CancellationToken cancellationToken)
    {
        var gymId = currentUser.RequireGymId();
        logger.LogInformation("Alta de staff solicitada en el gym {GymId}", gymId);

        var actorId = currentUser.UserId
            ?? throw new UnauthorizedDomainException("No hay usuario autenticado en el contexto.");

        // Sin perfil indicado, el de menos privilegio (Recepción).
        var profile = Domain.Identity.StaffProfile.Reception;
        if (request.Profile is not null)
        {
            PermissionName.TryParseProfile(request.Profile, out profile);
        }

        var ticket = await staff.CreateAsync(
            gymId, request.Email.Trim(), request.FullName.Trim(), profile, actorId, cancellationToken);

        // La cuenta ya existe: si el email falla, el usuario puede pedir el código con "olvidé mi contraseña".
        try
        {
            // Con cuenta previa solo se agregó el acceso: entra con su contraseña de siempre.
            await emailSender.SendAsync(
                ticket.Token is null
                    ? EmailTemplates.StaffAccessGranted(ticket.Account.Email, ticket.Account.FullName)
                    : EmailTemplates.StaffInvitation(ticket.Account.Email, ticket.Account.FullName, ticket.Token),
                cancellationToken);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "No se pudo enviar la invitación al staff {UserId}", ticket.Account.Id);
        }

        logger.LogInformation("Staff {UserId} creado en el gym {GymId}", ticket.Account.Id, gymId);
        return StaffMapping.ToResponse(ticket.Account);
    }
}

public sealed class DeactivateStaffHandler(
    ICurrentUserService currentUser,
    IStaffAccountService staff,
    ILogger<DeactivateStaffHandler> logger)
{
    public async Task<StaffResponse> HandleAsync(Guid userId, CancellationToken cancellationToken)
    {
        var gymId = currentUser.RequireGymId();
        var account = await staff.SetActiveAsync(gymId, userId, active: false, cancellationToken);
        logger.LogInformation("Staff {UserId} desactivado en el gym {GymId}", userId, gymId);
        return StaffMapping.ToResponse(account);
    }
}

public sealed class ReactivateStaffHandler(
    ICurrentUserService currentUser,
    IStaffAccountService staff,
    ILogger<ReactivateStaffHandler> logger)
{
    public async Task<StaffResponse> HandleAsync(Guid userId, CancellationToken cancellationToken)
    {
        var gymId = currentUser.RequireGymId();
        var account = await staff.SetActiveAsync(gymId, userId, active: true, cancellationToken);
        logger.LogInformation("Staff {UserId} reactivado en el gym {GymId}", userId, gymId);
        return StaffMapping.ToResponse(account);
    }
}

/// <summary>
/// Cambia el perfil y/o ajusta permisos sueltos de un staff (solo el owner, lo impone el controller).
/// Quitar un permiso cierra las sesiones de esa persona en este gym; agregar aplica al renovar el token.
/// </summary>
public sealed class UpdateStaffPermissionsHandler(
    ICurrentUserService currentUser,
    IStaffAccountService staff,
    ILogger<UpdateStaffPermissionsHandler> logger)
{
    public async Task<StaffResponse> HandleAsync(
        Guid userId, UpdateStaffPermissionsRequest request, CancellationToken cancellationToken)
    {
        var gymId = currentUser.RequireGymId();
        var actorId = currentUser.UserId
            ?? throw new UnauthorizedDomainException("No hay usuario autenticado en el contexto.");

        PermissionName.TryParseProfile(request.Profile, out var profile);
        var permissions = request.Permissions
            .Select(p => PermissionName.TryParse(p, out var permission)
                ? permission
                : throw new ValidationDomainException($"Permiso desconocido: '{p}'."))
            .Distinct()
            .ToList();

        var account = await staff.UpdateAccessAsync(gymId, userId, profile, permissions, actorId, cancellationToken);

        logger.LogInformation(
            "Perfil y permisos del staff {UserId} actualizados en el gym {GymId}: perfil {Profile}, {Count} permisos",
            userId, gymId, request.Profile, permissions.Count);
        return StaffMapping.ToResponse(account);
    }
}

/// <summary>Los perfiles predefinidos con los permisos que traen.</summary>
public sealed class ListStaffProfilesHandler
{
    public IReadOnlyList<StaffProfileInfo> Handle()
        => Enum.GetValues<Domain.Identity.StaffProfile>()
            .Select(p => new StaffProfileInfo(
                PermissionName.ToProfileName(p),
                Domain.Identity.StaffPermissions.ForProfile(p)
                    .OrderBy(x => x).Select(PermissionName.ToClaim).ToList()))
            .ToList();
}

/// <summary>Auditoría de perfiles y permisos del gym, o de una persona. Solo el owner.</summary>
public sealed class ListStaffAuditHandler(ICurrentUserService currentUser, IStaffAccountService staff)
{
    private const int MaxEntries = 200;

    public async Task<IReadOnlyList<StaffAuditResponse>> HandleAsync(Guid? userId, CancellationToken cancellationToken)
    {
        var entries = await staff.ListAuditAsync(currentUser.RequireGymId(), userId, MaxEntries, cancellationToken);
        return entries
            .Select(e => new StaffAuditResponse(
                e.Id,
                e.TargetUserId,
                e.TargetName,
                e.ActorUserId,
                e.ActorName,
                AuditActionName(e.Action),
                e.Permission is { } p ? PermissionName.ToClaim(p) : null,
                e.OldValue,
                e.NewValue,
                e.At))
            .ToList();
    }

    private static string AuditActionName(string action)
        => string.Concat(action.Select((c, i) => char.IsUpper(c) && i > 0 ? "_" + char.ToLowerInvariant(c) : char.ToLowerInvariant(c).ToString()));
}

internal static class StaffMapping
{
    public static StaffResponse ToResponse(StaffAccount account)
        => new(
            account.Id,
            account.Email,
            account.FullName,
            account.IsActive,
            PermissionName.ToProfileName(account.Profile),
            account.IsAdjusted,
            account.Permissions.Select(PermissionName.ToClaim).ToList());
}
