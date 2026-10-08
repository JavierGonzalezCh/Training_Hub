using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using GSM.Application.Common.Logs;
using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Routing;

namespace GSM.API.Middleware;

/// <summary>
/// Último eslabón de la cadena de <see cref="IExceptionHandler"/>: lo que no es una excepción de dominio es un
/// 500. Lo guarda en <c>logs.errors</c> (con huella, endpoint con plantilla y un resumen limpio de la petición) y
/// responde un <see cref="ProblemDetails"/> genérico con el <c>traceId</c>; el detalle real nunca sale al cliente.
/// </summary>
public sealed class UnhandledExceptionHandler(
    IProblemDetailsService problemDetailsService,
    ILogStore logStore,
    ILogger<UnhandledExceptionHandler> logger) : IExceptionHandler
{
    public async ValueTask<bool> TryHandleAsync(
        HttpContext httpContext,
        Exception exception,
        CancellationToken cancellationToken)
    {
        // El cliente se fue: no es un error del sistema.
        if (exception is OperationCanceledException && httpContext.RequestAborted.IsCancellationRequested)
        {
            httpContext.Response.StatusCode = 499;
            return true;
        }

        // Cuerpo malformado o demasiado grande detectado por Kestrel: es culpa de la petición.
        if (exception is BadHttpRequestException bad)
        {
            httpContext.Response.StatusCode = bad.StatusCode;
            return await WriteAsync(httpContext, exception, bad.StatusCode, "Solicitud inválida", null);
        }

        logger.LogError(exception, "Error no controlado en {Method} {Path}",
            httpContext.Request.Method, httpContext.Request.Path);

        if (!exception.Data.Contains(LoggedJobRunner.AlreadyLoggedKey))
        {
            try
            {
                var describer = RequestDescriber.Describe(httpContext);
                logStore.RecordError(new ErrorLogEntry(
                    ErrorSource.Api,
                    exception,
                    httpContext.Request.Method,
                    describer.Path,
                    describer.Endpoint,
                    await describer.SummaryAsync()));
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "No se pudo registrar el error no controlado");
            }
        }

        httpContext.Response.StatusCode = StatusCodes.Status500InternalServerError;
        return await WriteAsync(httpContext, exception, StatusCodes.Status500InternalServerError,
            "Error interno del servidor", "Ocurrió un error inesperado. Si persiste, indica el código de rastreo.");
    }

    private async ValueTask<bool> WriteAsync(
        HttpContext httpContext, Exception exception, int status, string title, string? detail)
    {
        var problem = new ProblemDetails
        {
            Status = status,
            Title = title,
            Detail = detail,
            Type = $"https://httpstatuses.io/{status}",
        };
        TraceIds.Stamp(httpContext, problem);

        return await problemDetailsService.TryWriteAsync(new ProblemDetailsContext
        {
            HttpContext = httpContext,
            Exception = exception,
            ProblemDetails = problem,
        });
    }
}

/// <summary>Descripción limpia de una petición para el log: ruta sin valores sensibles, plantilla y resumen.</summary>
public sealed class RequestDescriber
{
    private readonly HttpContext _context;
    private readonly RouteValueDictionary _routeValues;

    private RequestDescriber(HttpContext context, RouteValueDictionary routeValues, string path, string? endpoint)
    {
        _context = context;
        _routeValues = routeValues;
        Path = path;
        Endpoint = endpoint;
    }

    public string Path { get; }

    public string? Endpoint { get; }

    public static RequestDescriber Describe(HttpContext context)
    {
        // El middleware de excepciones borra el endpoint y los valores de ruta de la petición; los guarda aquí.
        var feature = context.Features.Get<IExceptionHandlerFeature>();
        var routeValues = feature?.RouteValues is { } saved
            ? new RouteValueDictionary(saved)
            : new RouteValueDictionary(context.Request.RouteValues);

        var path = context.Request.Path.Value ?? "/";
        foreach (var (key, value) in routeValues)
        {
            if (LogSanitizer.IsSensitive(key) && value?.ToString() is { Length: > 0 } raw)
            {
                path = path.Replace(raw, LogSanitizer.Redacted, StringComparison.Ordinal);
            }
        }

        var pattern = ((feature?.Endpoint ?? context.GetEndpoint()) as RouteEndpoint)?.RoutePattern.RawText;
        var endpoint = pattern is null ? null : pattern.StartsWith('/') ? pattern : "/" + pattern;
        return new RequestDescriber(context, routeValues, path, endpoint);
    }

    /// <summary>Parámetros de ruta y de consulta, y el cuerpo, todo limpio y recortado a 4 KB.</summary>
    public async Task<string> SummaryAsync()
    {
        var summary = new JsonObject();

        var route = new JsonObject();
        foreach (var (key, value) in _routeValues)
        {
            if (key is "controller" or "action")
            {
                continue;
            }

            route[key] = LogSanitizer.SanitizeValue(key, value?.ToString());
        }

        summary["route"] = route;

        var query = new JsonObject();
        foreach (var (key, values) in _context.Request.Query)
        {
            query[key] = LogSanitizer.SanitizeValue(key, values.ToString());
        }

        summary["query"] = query;

        var body = await ReadBodyAsync();
        if (body is not null)
        {
            summary["body"] = JsonNode.Parse(LogSanitizer.SanitizeJson(body));
        }

        return LogSanitizer.Truncate(summary.ToJsonString(), LogSanitizer.RequestSummaryMaxChars);
    }

    private async Task<string?> ReadBodyAsync()
    {
        var request = _context.Request;
        if (!request.Body.CanSeek || request.ContentLength is null or 0)
        {
            return null;
        }

        try
        {
            request.Body.Position = 0;
            using var reader = new StreamReader(request.Body, Encoding.UTF8, leaveOpen: true);
            var text = await reader.ReadToEndAsync();
            return string.IsNullOrWhiteSpace(text) ? null : text;
        }
        catch (Exception ex) when (ex is IOException or ObjectDisposedException or InvalidOperationException)
        {
            return null;
        }
    }
}
