using GSM.API.Auth;
using GSM.Application.Common.Abstractions;
using GSM.Application.SuperAdmin.Public;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;

namespace GSM.API.Filters;

/// <summary>
/// Bloqueo transversal de la API (como el multi-tenancy): estado comercial del gym y modo solo
/// lectura de la impersonación. Se comprueba una vez por petición (el <see cref="ISubscriptionReader"/>
/// cachea por request), no repartido por casos de uso. Filtro de autorización: corre después de que
/// la autenticación ha resuelto la identidad. El login es anónimo y se controla en <c>LoginHandler</c>.
/// </summary>
public sealed class SubscriptionGateFilter(ILogger<SubscriptionGateFilter> logger) : IAsyncAuthorizationFilter
{
    public async Task OnAuthorizationAsync(AuthorizationFilterContext context)
    {
        var http = context.HttpContext;
        var currentUser = http.RequestServices.GetRequiredService<ICurrentUserService>();

        if (currentUser.GymId is not { } gymId)
        {
            return;
        }

        var isExempt = context.ActionDescriptor.EndpointMetadata.OfType<SubscriptionGateExemptAttribute>().Any();
        var isRead = HttpMethods.IsGet(http.Request.Method) || HttpMethods.IsHead(http.Request.Method);

        if (currentUser.IsImpersonating)
        {
            // Auditoría: cada petición con el token de impersonación, no solo la apertura.
            logger.LogInformation(
                "Impersonación {SessionId}: {Method} {Path} por superadmin {UserId} en gym {GymId}",
                currentUser.ImpersonationSessionId, http.Request.Method, http.Request.Path,
                currentUser.UserId, gymId);

            // El token de impersonación solo sirve dentro del gym: /api/admin/* exige la sesión propia
            // del superadmin. La excepción es cerrar la impersonación, que se hace con este token.
            if (http.Request.Path.StartsWithSegments("/api/admin")
                && !http.Request.Path.StartsWithSegments("/api/admin/impersonate/end"))
            {
                context.Result = Forbidden(
                    "La sesión de impersonación solo sirve dentro del gimnasio. Termínala para volver al panel de plataforma.");
                return;
            }
        }

        var status = currentUser.IsSuperAdmin || isExempt
            ? null
            : await http.RequestServices.GetRequiredService<ISubscriptionReader>()
                .GetEffectiveStatusAsync(gymId, http.RequestAborted);

        var decision = SubscriptionGate.Evaluate(
            status, currentUser.IsSuperAdmin, currentUser.IsImpersonating, hasGym: true, isRead, isExempt,
            impersonationCanWrite: ImpersonationClaims.CanWrite(http.User));

        if (decision == SubscriptionGate.Decision.Allow)
        {
            return;
        }

        var detail = decision switch
        {
            SubscriptionGate.Decision.BlockedSuspended =>
                "La cuenta del gimnasio está suspendida. Contacta con soporte.",
            SubscriptionGate.Decision.BlockedImpersonation =>
                "La sesión de impersonación es de solo lectura: no se permite ninguna escritura. Abre una en modo escritura.",
            _ =>
                "La suscripción del gimnasio está inhabilitada por falta de pago: solo lectura hasta regularizar el pago.",
        };

        context.Result = Forbidden(detail);
    }

    private static ObjectResult Forbidden(string detail) => new(new ProblemDetails
    {
        Status = StatusCodes.Status403Forbidden,
        Title = "Operación no permitida",
        Detail = detail,
        Type = "https://httpstatuses.io/403",
    })
    {
        StatusCode = StatusCodes.Status403Forbidden,
    };
}
