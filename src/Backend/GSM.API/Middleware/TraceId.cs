using System.Diagnostics;
using GSM.Application.Common.Logs;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;

namespace GSM.API.Middleware;

/// <summary>Acceso al código de rastreo de la petición (guardado en <c>HttpContext.Items</c>).</summary>
public static class TraceIds
{
    public const string ItemKey = "gsm.traceId";
    public const string HeaderName = "X-Trace-Id";
    public const string ExtensionName = "traceId";

    public static string? Get(HttpContext? httpContext) =>
        httpContext?.Items.TryGetValue(ItemKey, out var value) == true ? value as string : null;

    /// <summary>Pone el <c>traceId</c> en un <see cref="ProblemDetails"/> (sustituye el que añade ASP.NET).</summary>
    public static void Stamp(HttpContext httpContext, ProblemDetails problem)
    {
        if (Get(httpContext) is { } traceId)
        {
            problem.Extensions[ExtensionName] = traceId;

            // El middleware de excepciones limpia las cabeceras de la respuesta: se repone aquí.
            if (!httpContext.Response.HasStarted)
            {
                httpContext.Response.Headers[HeaderName] = traceId;
            }
        }
    }
}

/// <summary>
/// Da a toda petición un <c>TraceId</c> (32 caracteres hexadecimales: el de la <see cref="Activity"/> si hay, o
/// uno propio), lo devuelve en la cabecera <c>X-Trace-Id</c> y lo pone en el contexto de log de Serilog. También
/// habilita el rebobinado del cuerpo JSON (hasta 1 MB) para poder guardar un resumen limpio si la petición acaba
/// en 500.
/// </summary>
public sealed class TraceIdMiddleware(RequestDelegate next)
{
    public const int MaxBufferedBodyBytes = 1_048_576;

    public async Task InvokeAsync(HttpContext context)
    {
        var traceId = Activity.Current?.TraceId.ToHexString() ?? Guid.NewGuid().ToString("N");
        context.Items[TraceIds.ItemKey] = traceId;
        context.Response.Headers[TraceIds.HeaderName] = traceId;

        if (context.Request.ContentLength is > 0 and <= MaxBufferedBodyBytes
            && context.Request.ContentType?.Contains("json", StringComparison.OrdinalIgnoreCase) == true)
        {
            context.Request.EnableBuffering();
        }

        using (Serilog.Context.LogContext.PushProperty("TraceId", traceId))
        {
            await next(context);
        }
    }
}

/// <summary><see cref="ITraceContext"/> sobre el <c>HttpContext</c> actual.</summary>
public sealed class HttpTraceContext(IHttpContextAccessor accessor) : ITraceContext
{
    public string? TraceId => TraceIds.Get(accessor.HttpContext);
}

/// <summary>
/// Filtro global de resultado: todo <see cref="ProblemDetails"/> (incluido el <c>ValidationProblemDetails</c> del
/// <c>ValidationActionFilter</c> y los que arma MVC) sale con el <c>traceId</c> del código de rastreo.
/// </summary>
public sealed class TraceIdResultFilter : IAlwaysRunResultFilter
{
    public void OnResultExecuting(ResultExecutingContext context)
    {
        if (context.Result is ObjectResult { Value: ProblemDetails problem })
        {
            TraceIds.Stamp(context.HttpContext, problem);
        }
    }

    public void OnResultExecuted(ResultExecutedContext context)
    {
    }
}
