using GSM.Domain.Common.Exceptions;
using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.Mvc;

namespace GSM.API.Middleware;

/// <summary>
/// Traduce la jerarquía <see cref="DomainException"/> a <see cref="ProblemDetails"/> (RFC 7807).
/// Todo lo que no derive de <see cref="DomainException"/> se deja pasar para caer en 500
/// con mensaje genérico (el detalle real queda en el log de Serilog).
/// </summary>
public sealed class DomainExceptionHandler(
    IProblemDetailsService problemDetailsService,
    ILogger<DomainExceptionHandler> logger) : IExceptionHandler
{
    public async ValueTask<bool> TryHandleAsync(
        HttpContext httpContext,
        Exception exception,
        CancellationToken cancellationToken)
    {
        if (exception is not DomainException domainException)
        {
            return false;
        }

        var (status, title) = Map(domainException);

        logger.LogWarning(domainException,
            "Excepción de dominio {ExceptionType} → {StatusCode}", domainException.GetType().Name, status);

        httpContext.Response.StatusCode = status;

        return await problemDetailsService.TryWriteAsync(new ProblemDetailsContext
        {
            HttpContext = httpContext,
            Exception = domainException,
            ProblemDetails = new ProblemDetails
            {
                Status = status,
                Title = title,
                Detail = domainException.Message,
                Type = $"https://httpstatuses.io/{status}",
            },
        });
    }

    private static (int Status, string Title) Map(DomainException exception) => exception switch
    {
        ValidationDomainException => (StatusCodes.Status422UnprocessableEntity, "Invariante de negocio violada"),
        NotFoundException => (StatusCodes.Status404NotFound, "Recurso no encontrado"),
        ConflictException => (StatusCodes.Status409Conflict, "Conflicto de estado"),
        ForbiddenDomainException => (StatusCodes.Status403Forbidden, "Operación no permitida"),
        _ => (StatusCodes.Status400BadRequest, "Solicitud inválida"),
    };
}
