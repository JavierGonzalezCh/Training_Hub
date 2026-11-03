using GSM.Application.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;

namespace GSM.API.Filters;

/// <summary>
/// FL1: quien entró con la clave provisional (su documento) no usa la API hasta crear su contraseña. Mira el
/// claim <c>pwd_change</c> del token; las acciones con <see cref="AllowWhilePasswordChangeRequiredAttribute"/>
/// pasan. Es un filtro transversal, como el bloqueo por suscripción.
/// </summary>
public sealed class PasswordChangeGateFilter : IAuthorizationFilter
{
    public const string ProblemCode = "password_change_required";

    public void OnAuthorization(AuthorizationFilterContext context)
    {
        if (context.HttpContext.User.FindFirst(GsmClaims.PasswordChangeRequired)?.Value != "true")
        {
            return;
        }

        if (context.ActionDescriptor.EndpointMetadata.OfType<AllowWhilePasswordChangeRequiredAttribute>().Any())
        {
            return;
        }

        var problem = new ProblemDetails
        {
            Status = StatusCodes.Status403Forbidden,
            Title = "Crea tu contraseña",
            Detail = "Antes de continuar, crea tu contraseña.",
        };
        problem.Extensions["code"] = ProblemCode;
        context.Result = new ObjectResult(problem) { StatusCode = StatusCodes.Status403Forbidden };
    }
}
