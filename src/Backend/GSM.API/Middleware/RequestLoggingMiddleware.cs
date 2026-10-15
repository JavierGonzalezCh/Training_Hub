using System.Diagnostics;
using GSM.Application.Common.Logs;

namespace GSM.API.Middleware;

/// <summary>
/// LG2: la petición completa a la vista del log. Envuelve al manejador de excepciones, así que ve el código de
/// estado definitivo. Registra (siempre por <see cref="ILogStore"/>, que nunca espera ni lanza):
/// <list type="bullet">
/// <item><c>logs.slow_requests</c>: toda petición que tarda más de <c>Logs:SlowRequestMs</c> (2000 por defecto).</item>
/// <item><c>logs.business_rejections</c>: los 400/403/404/409/422 que nadie registró antes con su causa (las
/// excepciones de dominio y la validación de formularios ya lo hicieron y lo marcan con
/// <see cref="RejectionLogging.LoggedItemKey"/>): un 403 de política, un 400 de enlace de modelo, un 404
/// de recurso. Solo si la ruta existe: los 404 de rutas inexistentes (robots, escaneos) son ruido.</item>
/// </list>
/// </summary>
public sealed class RequestLoggingMiddleware(
    RequestDelegate next,
    ILogStore logStore,
    IConfiguration configuration,
    ILogger<RequestLoggingMiddleware> logger)
{
    public const int DefaultSlowRequestMs = 2000;

    private readonly int _slowRequestMs = configuration.GetValue("Logs:SlowRequestMs", DefaultSlowRequestMs);

    public async Task InvokeAsync(HttpContext context)
    {
        var started = Stopwatch.GetTimestamp();
        try
        {
            await next(context);
        }
        finally
        {
            Record(context, Stopwatch.GetElapsedTime(started));
        }
    }

    private void Record(HttpContext context, TimeSpan elapsed)
    {
        try
        {
            var status = context.Response.StatusCode;
            var durationMs = (int)Math.Min(elapsed.TotalMilliseconds, int.MaxValue);
            var isSlow = durationMs > _slowRequestMs;
            var isRejection = RejectionLogging.IsRejectionStatus(status) && !RejectionLogging.WasLogged(context);
            if (!isSlow && !isRejection)
            {
                return;
            }

            var endpoint = RequestDescriber.Describe(context).Endpoint;

            if (isSlow)
            {
                logStore.RecordSlowRequest(new SlowRequestLogEntry(context.Request.Method, endpoint, durationMs, status));
            }

            if (isRejection && endpoint is not null)
            {
                logStore.RecordBusinessRejection(new BusinessRejectionLogEntry(
                    context.Request.Method, endpoint, status, "http_status", ReasonPhrase(status), null));
            }
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "No se pudo registrar la petición en el log");
        }
    }

    private static string ReasonPhrase(int status) => status switch
    {
        StatusCodes.Status400BadRequest => "Solicitud inválida",
        StatusCodes.Status403Forbidden => "Operación no permitida",
        StatusCodes.Status404NotFound => "Recurso no encontrado",
        StatusCodes.Status409Conflict => "Conflicto de estado",
        StatusCodes.Status422UnprocessableEntity => "Invariante de negocio violada",
        _ => status.ToString(),
    };
}

/// <summary>Utilidades para registrar rechazos de negocio sin duplicarlos (§4.2).</summary>
public static class RejectionLogging
{
    public const string LoggedItemKey = "gsm.rejectionLogged";

    public static bool IsRejectionStatus(int status) =>
        status is StatusCodes.Status400BadRequest or StatusCodes.Status403Forbidden
            or StatusCodes.Status404NotFound or StatusCodes.Status409Conflict
            or StatusCodes.Status422UnprocessableEntity;

    public static bool WasLogged(HttpContext context) => context.Items.ContainsKey(LoggedItemKey);

    /// <summary>Marca la petición como ya registrada para que el middleware no la repita.</summary>
    public static void MarkLogged(HttpContext context) => context.Items[LoggedItemKey] = true;
}

/// <summary><see cref="IClientInfo"/> sobre el <c>HttpContext</c> actual.</summary>
public sealed class HttpClientInfo(IHttpContextAccessor accessor) : IClientInfo
{
    public string? Ip => accessor.HttpContext?.Connection.RemoteIpAddress?.ToString();

    public string? UserAgent => accessor.HttpContext?.Request.Headers.UserAgent.ToString() is { Length: > 0 } ua ? ua : null;
}
