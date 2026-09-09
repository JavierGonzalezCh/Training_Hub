using GSM.Application.Common.Abstractions;
using GSM.Application.SuperAdmin.Public;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;

namespace GSM.API.Filters;

/// <summary>
/// Bloqueo por estado comercial del gym, transversal como el multi-tenancy: se comprueba una sola
/// vez por petición (el <see cref="ISubscriptionReader"/> cachea por request) y no repartido por
/// casos de uso. Corre como filtro de autorización, después de que la autenticación ha resuelto la
/// identidad. El login es anónimo y se controla en <c>LoginHandler</c>.
/// </summary>
public sealed class SubscriptionGateFilter : IAsyncAuthorizationFilter
{
    public async Task OnAuthorizationAsync(AuthorizationFilterContext context)
    {
        var http = context.HttpContext;
        var currentUser = http.RequestServices.GetRequiredService<ICurrentUserService>();

        // Atajo: sin gym ni superadmin no hay nada que consultar.
        if (currentUser.GymId is not { } gymId)
        {
            return;
        }

        var isExempt = context.ActionDescriptor.EndpointMetadata.OfType<SubscriptionGateExemptAttribute>().Any();
        var isRead = HttpMethods.IsGet(http.Request.Method) || HttpMethods.IsHead(http.Request.Method);

        var status = currentUser.IsSuperAdmin || isExempt
            ? null
            : await http.RequestServices.GetRequiredService<ISubscriptionReader>()
                .GetEffectiveStatusAsync(gymId, http.RequestAborted);

        var decision = SubscriptionGate.Evaluate(
            status, currentUser.IsSuperAdmin, hasGym: true, isRead, isExempt);

        if (decision == SubscriptionGate.Decision.Allow)
        {
            return;
        }

        var detail = decision == SubscriptionGate.Decision.BlockedSuspended
            ? "La cuenta del gimnasio está suspendida. Contacta con soporte."
            : "La suscripción del gimnasio está inhabilitada por falta de pago: solo lectura hasta regularizar el pago.";

        context.Result = new ObjectResult(new ProblemDetails
        {
            Status = StatusCodes.Status403Forbidden,
            Title = "Suscripción del gimnasio no vigente",
            Detail = detail,
            Type = "https://httpstatuses.io/403",
        })
        {
            StatusCode = StatusCodes.Status403Forbidden,
        };
    }
}
