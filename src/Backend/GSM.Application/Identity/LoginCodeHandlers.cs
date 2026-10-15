using GSM.Application.Common.Abstractions;
using GSM.Application.Common.Email;
using GSM.Application.Gyms.Public;
using GSM.Application.Identity.Abstractions;
using GSM.Application.Identity.Contracts;
using GSM.Domain.Common.Exceptions;
using GSM.Domain.Identity;
using Microsoft.Extensions.Logging;

namespace GSM.Application.Identity;

/// <summary>
/// Minutos de vigencia que se anuncian en el correo del código; coinciden con <c>LoginCode.Lifetime</c>
/// (Infrastructure).
/// </summary>
public static class LoginCodePolicy
{
    public const int ValidityMinutes = 10;
}

/// <summary>
/// <c>POST /api/auth/request-login-code</c>: manda el código de 6 dígitos al correo del miembro (Identity §6).
/// Sale igual exista o no la cuenta, esté limitada o no: no revela nada. El código nunca se escribe en el log.
/// </summary>
public sealed class RequestLoginCodeHandler(
    ILoginCodeService loginCodes,
    IGymReader gyms,
    IEmailSender emailSender,
    ILogger<RequestLoginCodeHandler> logger)
{
    public async Task HandleAsync(RequestLoginCodeRequest request, CancellationToken cancellationToken)
    {
        var issue = await loginCodes.IssueAsync(request.Email.Trim(), cancellationToken);

        switch (issue.Status)
        {
            case LoginCodeIssueStatus.NotEligible:
                logger.LogInformation("Código de acceso pedido para un email sin cuenta de miembro activa");
                return;
            case LoginCodeIssueStatus.Throttled:
                logger.LogInformation("Código de acceso pedido por encima del límite (1 por minuto, 5 por hora)");
                return;
        }

        string? gymName = null;
        if (!string.IsNullOrWhiteSpace(request.GymSlug))
        {
            gymName = (await gyms.GetBrandingBySlugAsync(request.GymSlug, cancellationToken))?.Name;
        }

        try
        {
            await emailSender.SendAsync(
                EmailTemplates.LoginCode(
                    issue.Email!, issue.FullName!, issue.Code!, gymName, LoginCodePolicy.ValidityMinutes),
                cancellationToken);
            logger.LogInformation("Código de acceso enviado a {UserEmail}", issue.Email);
        }
        catch (Exception ex)
        {
            // Si el correo falla, la respuesta no cambia (no se revela la cuenta) y la persona puede pedir otro.
            logger.LogError(ex, "No se pudo enviar el código de acceso a {UserEmail}", issue.Email);
        }
    }
}

/// <summary>
/// <c>POST /api/auth/verify-login-code</c>: entra con el código. Igual que el login, con un solo gym deja la
/// sesión iniciada y con varios pide elegir; solo abre accesos de miembro. Código malo, vencido, agotado o
/// de una cuenta sin acceso de miembro: 401 con el mismo mensaje.
/// </summary>
public sealed class VerifyLoginCodeHandler(
    ILoginCodeService loginCodes,
    SessionIssuer sessions,
    ILogger<VerifyLoginCodeHandler> logger)
{
    public async Task<LoginResponse> HandleAsync(VerifyLoginCodeRequest request, CancellationToken cancellationToken)
    {
        var account = await loginCodes.VerifyAsync(request.Email.Trim(), request.Code, cancellationToken)
            ?? throw new UnauthorizedDomainException("El código no es válido o venció. Pide uno nuevo.");

        logger.LogInformation("Login con código correcto para {UserId}", account.Id);
        return await sessions.IssueLoginAsync(account, cancellationToken, UserRole.Member);
    }
}

/// <summary>
/// <c>GET /api/public/gyms/{slug}/branding</c>: logo y colores del gym para la pantalla de entrada del
/// miembro, sin sesión. Solo marca pública, nada del negocio.
/// </summary>
public sealed class GetPublicGymBrandingHandler(IGymReader gyms)
{
    public async Task<PublicGymBrandingResponse> HandleAsync(string slug, CancellationToken cancellationToken)
    {
        var branding = await gyms.GetBrandingBySlugAsync(slug, cancellationToken)
            ?? throw new NotFoundException("El gimnasio no existe.");
        return new PublicGymBrandingResponse(
            branding.Name, branding.LogoUrl, branding.PrimaryColor, branding.SecondaryColor);
    }
}
