using System.Text.Json;
using FluentValidation;
using GSM.API.Middleware;
using GSM.Application.Common.Logs;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;

namespace GSM.API.Filters;

/// <summary>
/// Filtro global: por cada argumento de acción con un <see cref="IValidator{T}"/> registrado,
/// ejecuta la validación de FluentValidation. Si falla, corta con 400 + <see cref="ValidationProblemDetails"/>.
/// </summary>
public sealed class ValidationActionFilter(IServiceProvider serviceProvider, ILogStore logStore) : IAsyncActionFilter
{
    public async Task OnActionExecutionAsync(ActionExecutingContext context, ActionExecutionDelegate next)
    {
        foreach (var argument in context.ActionArguments.Values)
        {
            if (argument is null)
            {
                continue;
            }

            var validatorType = typeof(IValidator<>).MakeGenericType(argument.GetType());

            if (serviceProvider.GetService(validatorType) is not IValidator validator)
            {
                continue;
            }

            var validationContext = new ValidationContext<object>(argument);
            var result = await validator.ValidateAsync(validationContext, context.HttpContext.RequestAborted);

            if (result.IsValid)
            {
                continue;
            }

            var errors = result.Errors
                .GroupBy(e => e.PropertyName)
                .ToDictionary(g => g.Key, g => g.Select(e => e.ErrorMessage).ToArray());

            // Rechazo de negocio (§4.2): solo los nombres de los campos, nunca los valores.
            logStore.RecordBusinessRejection(new BusinessRejectionLogEntry(
                context.HttpContext.Request.Method,
                RequestDescriber.Describe(context.HttpContext).Endpoint,
                StatusCodes.Status400BadRequest,
                "validation",
                "Validación de formulario",
                null,
                JsonSerializer.Serialize(errors.Keys)));
            RejectionLogging.MarkLogged(context.HttpContext);

            context.Result = new BadRequestObjectResult(new ValidationProblemDetails(errors));
            return;
        }

        await next();
    }
}
