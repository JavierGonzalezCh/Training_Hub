using GSM.Application.Common.Abstractions;
using GSM.Application.SuperAdmin.Abstractions;
using Microsoft.AspNetCore.Mvc.Controllers;

namespace GSM.API.Middleware;

/// <summary>
/// Auditoría de la plataforma (S6): cada petición hecha con un token de impersonación (consulta o escritura)
/// deja una fila en <c>superadmin.impersonation_actions</c> con el método, la plantilla del endpoint, un
/// resumen y el código de estado. Va después de la autorización para conocer la identidad. Una petición
/// normal de staff no deja nada. Guardar no frena la petición: el registrador nunca lanza.
/// </summary>
public sealed class ImpersonationAuditMiddleware(RequestDelegate next)
{
    public async Task InvokeAsync(HttpContext context)
    {
        var currentUser = context.RequestServices.GetRequiredService<ICurrentUserService>();
        var sessionId = currentUser.IsImpersonating ? currentUser.ImpersonationSessionId : null;

        var failed = false;
        try
        {
            await next(context);
        }
        catch
        {
            failed = true; // el manejador global responderá 500 después de que esto pase
            throw;
        }
        finally
        {
            if (sessionId is { } id)
            {
                var endpoint = context.GetEndpoint();
                var template = (endpoint as RouteEndpoint)?.RoutePattern.RawText ?? context.Request.Path.Value ?? "?";
                var action = endpoint?.Metadata.GetMetadata<ControllerActionDescriptor>();
                var summary = action is null ? template : $"{action.ControllerName}.{action.ActionName}";

                await context.RequestServices.GetRequiredService<IImpersonationActionRecorder>().RecordAsync(
                    id, context.Request.Method, template, summary,
                    failed ? StatusCodes.Status500InternalServerError : context.Response.StatusCode);
            }
        }
    }
}
