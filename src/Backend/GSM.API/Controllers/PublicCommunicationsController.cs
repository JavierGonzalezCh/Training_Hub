using GSM.API.Filters;
using GSM.Application.Notifications;
using GSM.Domain.Common.Exceptions;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace GSM.API.Controllers;

/// <summary>
/// Enlace de baja de los correos de comunicaciones: se abre desde el email, sin iniciar sesión, así
/// que responde una página HTML mínima en lugar de JSON.
/// </summary>
[ApiController]
[Route("api/public")]
[AllowAnonymous]
[SubscriptionGateExempt]
public sealed class PublicCommunicationsController : ControllerBase
{
    [HttpGet("unsubscribe")]
    [Produces("text/html")]
    public async Task<ContentResult> Unsubscribe(
        [FromQuery] string? t, [FromServices] UnsubscribeHandler handler, CancellationToken cancellationToken)
    {
        try
        {
            var category = await handler.HandleAsync(t, cancellationToken);
            var what = category is null ? "avisos del gimnasio" : $"correos de «{category}»";
            return Page(200, "Listo", $"No recibirás más {what} por email. Puedes volver a activarlos desde las preferencias de la app.");
        }
        catch (ValidationDomainException)
        {
            return Page(400, "Enlace no válido", "Este enlace de baja no es válido. Puedes desactivar los avisos por email desde las preferencias de la app.");
        }
    }

    private static ContentResult Page(int status, string title, string message) => new()
    {
        StatusCode = status,
        ContentType = "text/html; charset=utf-8",
        Content = $"""
            <!doctype html>
            <html lang="es"><head><meta charset="utf-8"><meta name="viewport" content="width=device-width, initial-scale=1">
            <title>{title} — GSM</title></head>
            <body style="font-family:system-ui,sans-serif;max-width:32rem;margin:4rem auto;padding:0 1rem;line-height:1.6">
            <h1 style="font-size:1.5rem">{title}</h1><p>{message}</p></body></html>
            """,
    };
}
