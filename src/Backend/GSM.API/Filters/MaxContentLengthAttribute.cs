using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;

namespace GSM.API.Filters;

/// <summary>
/// Rechaza con 413 una petición cuyo <c>Content-Length</c> supera el tope, <b>antes</b> de leer el cuerpo ni
/// validarlo. Complementa a <c>[RequestSizeLimit]</c> (que impone Kestrel al leer el cuerpo, sea cual sea el
/// <c>Content-Length</c>) para los endpoints anónimos que alguien podría usar para llenar la base.
/// </summary>
[AttributeUsage(AttributeTargets.Class | AttributeTargets.Method)]
public sealed class MaxContentLengthAttribute(long maxBytes) : Attribute, IResourceFilter
{
    public void OnResourceExecuting(ResourceExecutingContext context)
    {
        if (context.HttpContext.Request.ContentLength > maxBytes)
        {
            context.Result = new ObjectResult(new ProblemDetails
            {
                Status = StatusCodes.Status413PayloadTooLarge,
                Title = "La petición es demasiado grande.",
            })
            {
                StatusCode = StatusCodes.Status413PayloadTooLarge,
            };
        }
    }

    public void OnResourceExecuted(ResourceExecutedContext context)
    {
    }
}
