using GSM.Application.Common.Logs;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Authorization.Policy;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.RateLimiting;

namespace GSM.API.Auth;

/// <summary>
/// Puntos de enganche de <c>logs.security_events</c> (docs/GSM-logs.md §4.7) que viven en la API: el token
/// inválido (<c>invalid_token</c>), el acceso denegado por rol o permiso (<c>permission_denied</c>) y el límite
/// de peticiones (<c>rate_limited</c>). Todos van por <see cref="ILogStore"/>: no esperan ni lanzan, y nunca
/// guardan el token ni la cabecera <c>Authorization</c>.
/// </summary>
public static class SecurityEventLogging
{
    /// <summary>401 por token vencido, mal firmado o revocado (una petición sin token no pasa por aquí).</summary>
    public static JwtBearerEvents JwtEvents() => new()
    {
        OnAuthenticationFailed = context =>
        {
            Record(context.HttpContext, SecurityEventTypes.InvalidToken,
                $"{context.Exception.GetType().Name} en {Describe(context.HttpContext)}");
            return Task.CompletedTask;
        },
    };

    /// <summary>429: el limitador rechazó la petición.</summary>
    public static Task OnRateLimited(OnRejectedContext context, CancellationToken cancellationToken)
    {
        Record(context.HttpContext, SecurityEventTypes.RateLimited, Describe(context.HttpContext));
        return Task.CompletedTask;
    }

    internal static void Record(HttpContext context, string eventType, string? detail)
    {
        try
        {
            context.RequestServices.GetRequiredService<ILogStore>()
                .RecordSecurityEvent(new SecurityEventLogEntry(eventType, detail));
        }
        catch
        {
            // el log nunca tumba la petición
        }
    }

    internal static string Describe(HttpContext context)
    {
        var pattern = (context.GetEndpoint() as RouteEndpoint)?.RoutePattern.RawText ?? context.Request.Path.Value;
        return $"{context.Request.Method} {pattern}";
    }
}

/// <summary>
/// Decora el resultado de autorización por defecto: igual en todo, pero cuando el usuario está autenticado y la
/// política lo rechaza (403 por rol o permiso) deja un <c>permission_denied</c>.
/// </summary>
public sealed class LoggingAuthorizationResultHandler : IAuthorizationMiddlewareResultHandler
{
    private readonly AuthorizationMiddlewareResultHandler _default = new();

    public Task HandleAsync(
        RequestDelegate next, HttpContext context, AuthorizationPolicy policy, PolicyAuthorizationResult authorizeResult)
    {
        if (authorizeResult.Forbidden)
        {
            SecurityEventLogging.Record(context, SecurityEventTypes.PermissionDenied, SecurityEventLogging.Describe(context));
        }

        return _default.HandleAsync(next, context, policy, authorizeResult);
    }
}
