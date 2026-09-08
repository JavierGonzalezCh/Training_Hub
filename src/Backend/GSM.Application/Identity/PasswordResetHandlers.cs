using GSM.Application.Common.Abstractions;
using GSM.Application.Common.Email;
using GSM.Application.Identity.Abstractions;
using GSM.Application.Identity.Contracts;
using Microsoft.Extensions.Logging;

namespace GSM.Application.Identity;

public sealed class ForgotPasswordHandler(
    IPasswordResetService passwordReset,
    IEmailSender emailSender,
    ILogger<ForgotPasswordHandler> logger)
{
    public async Task HandleAsync(ForgotPasswordRequest request, CancellationToken cancellationToken)
    {
        var email = request.Email.Trim();
        var ticket = await passwordReset.CreateTokenAsync(email, cancellationToken);

        if (ticket is null)
        {
            logger.LogInformation("Restablecimiento solicitado para un email sin cuenta activa");
            return; // Sin revelar si el email existe.
        }

        await emailSender.SendAsync(
            EmailTemplates.PasswordReset(ticket.Email, ticket.Token, resetUrl: null), cancellationToken);
        logger.LogInformation("Token de restablecimiento enviado a {UserEmail}", ticket.Email);
    }
}

public sealed class ResetPasswordHandler(IPasswordResetService passwordReset)
{
    public Task HandleAsync(ResetPasswordRequest request, CancellationToken cancellationToken)
        => passwordReset.ResetAsync(
            request.Email.Trim(), request.Token, request.NewPassword, cancellationToken);
}
