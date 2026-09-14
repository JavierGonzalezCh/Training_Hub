using GSM.Application.Common.Abstractions;
using GSM.Application.Common.Email;
using GSM.Application.Identity.Abstractions;
using GSM.Application.Identity.Contracts;
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

        var ticket = await staff.CreateAsync(gymId, request.Email.Trim(), request.FullName.Trim(), cancellationToken);

        // La cuenta ya existe: si el email falla, el usuario puede pedir el código con "olvidé mi contraseña".
        try
        {
            await emailSender.SendAsync(
                EmailTemplates.StaffInvitation(ticket.Account.Email, ticket.Account.FullName, ticket.Token),
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

internal static class StaffMapping
{
    public static StaffResponse ToResponse(StaffAccount account)
        => new(account.Id, account.Email, account.FullName, account.IsActive);
}
