using GSM.Application.Common.Abstractions;
using GSM.Application.Common.Email;
using GSM.Application.Common.Events;
using GSM.Application.Identity.Abstractions;
using GSM.Application.Identity.Contracts;
using GSM.Application.Identity.Events;
using GSM.Domain.Common.Exceptions;
using Microsoft.Extensions.Logging;

namespace GSM.Application.Identity;

/// <summary>Minutos de vigencia que se anuncian en el correo; coinciden con <c>EmailChangeRequest.Lifetime</c>.</summary>
public static class EmailChangePolicy
{
    public const int ValidityMinutes = 10;
}

/// <summary>
/// <c>POST /api/me/request-email-change</c>: manda un código de 6 dígitos al correo NUEVO (Identity §3.4). El email de la
/// cuenta no cambia hasta confirmarlo. Cualquier rol con sesión; no se permite mientras se impersona. El código
/// nunca se escribe en el log.
/// </summary>
public sealed class RequestEmailChangeHandler(
    ICurrentUserService currentUser,
    IEmailChangeService emailChanges,
    IEmailSender emailSender,
    ILogger<RequestEmailChangeHandler> logger)
{
    public async Task HandleAsync(RequestEmailChangeRequest request, CancellationToken cancellationToken)
    {
        var userId = currentUser.UserId
            ?? throw new UnauthorizedDomainException("No hay usuario autenticado en el contexto.");

        if (currentUser.IsImpersonating)
        {
            throw new ForbiddenDomainException("No se puede cambiar el email mientras se impersona un gimnasio.");
        }

        var issue = await emailChanges.IssueAsync(userId, request.NewEmail, cancellationToken);
        switch (issue.Status)
        {
            case EmailChangeIssueStatus.AccountUnavailable:
                throw new UnauthorizedDomainException("La cuenta no está disponible.");
            case EmailChangeIssueStatus.SameEmail:
                throw new ValidationDomainException("Ese ya es el email de tu cuenta.");
            case EmailChangeIssueStatus.EmailTaken:
                throw new ConflictException("Ese email ya está en uso por otra cuenta.");
            case EmailChangeIssueStatus.Throttled:
                throw new ConflictException(
                    "Pediste demasiados códigos. Espera un minuto (máximo 5 por hora) e inténtalo de nuevo.");
        }

        // Si el correo falla no hay pedido útil: se avisa para que la persona pueda reintentar.
        await emailSender.SendAsync(
            EmailTemplates.EmailChangeCode(
                issue.NewEmail!, issue.FullName!, issue.Code!, EmailChangePolicy.ValidityMinutes),
            cancellationToken);
        logger.LogInformation("Código de cambio de email enviado para la cuenta {UserId}", userId);
    }
}

/// <summary>
/// <c>POST /api/me/confirm-email-change</c>: confirma el código, cambia el email de la cuenta, publica
/// <see cref="UserEmailChanged"/> (Members sincroniza las fichas) y avisa al correo viejo. Código malo, vencido o
/// agotado: 422 (no 401: la sesión sigue siendo válida).
/// </summary>
public sealed class ConfirmEmailChangeHandler(
    ICurrentUserService currentUser,
    IEmailChangeService emailChanges,
    IEmailSender emailSender,
    IDomainEventPublisher events,
    ILogger<ConfirmEmailChangeHandler> logger)
{
    public async Task<EmailChangedResponse> HandleAsync(
        ConfirmEmailChangeRequest request, CancellationToken cancellationToken)
    {
        var userId = currentUser.UserId
            ?? throw new UnauthorizedDomainException("No hay usuario autenticado en el contexto.");

        if (currentUser.IsImpersonating)
        {
            throw new ForbiddenDomainException("No se puede cambiar el email mientras se impersona un gimnasio.");
        }

        var result = await emailChanges.ConfirmAsync(userId, request.Code, cancellationToken);
        switch (result.Status)
        {
            case EmailChangeConfirmationStatus.AccountUnavailable:
                throw new UnauthorizedDomainException("La cuenta no está disponible.");
            case EmailChangeConfirmationStatus.InvalidCode:
                throw new ValidationDomainException("El código no es válido o venció. Pide uno nuevo.");
            case EmailChangeConfirmationStatus.EmailTaken:
                throw new ConflictException("Ese email ya está en uso por otra cuenta.");
        }

        logger.LogInformation("Email de la cuenta {UserId} cambiado", userId);
        await events.PublishAsync(new UserEmailChanged(userId, result.OldEmail!, result.NewEmail!), cancellationToken);

        try
        {
            await emailSender.SendAsync(
                EmailTemplates.EmailChangedNotice(result.OldEmail!, result.FullName!, result.NewEmail!),
                cancellationToken);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "No se pudo avisar del cambio de email a la cuenta {UserId}", userId);
        }

        return new EmailChangedResponse(result.NewEmail!);
    }
}
